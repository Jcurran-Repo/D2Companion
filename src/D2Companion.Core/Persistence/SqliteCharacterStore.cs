using System.Text.Json;
using D2Companion.Core.Domain;
using D2Companion.Core.Ledger;
using Microsoft.Data.Sqlite;

namespace D2Companion.Core.Persistence;

/// <summary>
/// SQLite-backed store. The character aggregate is stored as a single JSON row
/// (it's one small object that Claude owns end-to-end), while the ledger is stored
/// as real rows so a history / undo view can query it later.
/// </summary>
public sealed class SqliteCharacterStore : ICharacterStore
{
    private readonly string _connectionString;

    public SqliteCharacterStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));

        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureSchema();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS character (
                id          INTEGER PRIMARY KEY CHECK (id = 1),
                json        TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ledger (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp_utc TEXT    NOT NULL,
                source        INTEGER NOT NULL,
                action        TEXT    NOT NULL,
                details       TEXT    NOT NULL,
                rationale     TEXT    NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public Character LoadCharacter()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM character WHERE id = 1;";
        var json = command.ExecuteScalar() as string;
        if (string.IsNullOrEmpty(json))
            return new Character();

        return JsonSerializer.Deserialize<Character>(json, D2Json.Options) ?? new Character();
    }

    public void SaveCharacter(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);

        var json = JsonSerializer.Serialize(character, D2Json.Options);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO character (id, json, updated_utc)
            VALUES (1, $json, $updated)
            ON CONFLICT(id) DO UPDATE SET json = excluded.json, updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void AppendLedger(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ledger (timestamp_utc, source, action, details, rationale)
            VALUES ($timestamp, $source, $action, $details, $rationale)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$timestamp", entry.TimestampUtc.ToString("O"));
        command.Parameters.AddWithValue("$source", (int)entry.Source);
        command.Parameters.AddWithValue("$action", entry.Action);
        command.Parameters.AddWithValue("$details", entry.Details);
        command.Parameters.AddWithValue("$rationale", (object?)entry.Rationale ?? DBNull.Value);
        entry.Id = (long)(command.ExecuteScalar() ?? 0L);
    }

    public void ClearAll()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM character;
            DELETE FROM ledger;
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<LedgerEntry> LoadLedger(int max = 200)
    {
        if (max <= 0) return Array.Empty<LedgerEntry>();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, timestamp_utc, source, action, details, rationale
            FROM ledger
            ORDER BY id DESC
            LIMIT $max;
            """;
        command.Parameters.AddWithValue("$max", max);

        var entries = new List<LedgerEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new LedgerEntry
            {
                Id = reader.GetInt64(0),
                TimestampUtc = DateTimeOffset.Parse(reader.GetString(1)),
                Source = (DecisionSource)reader.GetInt32(2),
                Action = reader.GetString(3),
                Details = reader.GetString(4),
                Rationale = reader.IsDBNull(5) ? null : reader.GetString(5),
            });
        }
        return entries;
    }
}
