using System.IO;
using System.Text.Json;

namespace D2Companion.Brain.Reference;

/// <summary>
/// Loads the editable seed-site list from reference-sites.json, creating it with defaults on
/// first run so the player has a file to edit. Any read/parse trouble falls back to defaults
/// rather than breaking the brain.
/// </summary>
public static class ReferenceConfig
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static ReferenceOptions LoadOrCreate(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<ReferenceOptions>(File.ReadAllText(path), Json);
                if (loaded is { Sources.Count: > 0 })
                    return loaded;
            }
            else
            {
                var defaults = new ReferenceOptions();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(defaults, Json));
                return defaults;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Config is optional — a missing/broken file just means "use the defaults".
        }

        return new ReferenceOptions();
    }

    public static void Save(string path, ReferenceOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(options, Json));
    }
}
