using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using D2Companion.Core.Domain;
using D2Companion.Core.Ledger;

namespace D2Companion.Core.Export;

/// <summary>A point-in-time snapshot of the character and its full decision log.</summary>
public sealed record CharacterExport(
    DateTimeOffset ExportedUtc,
    Character Character,
    IReadOnlyList<LedgerEntry> Ledger);

/// <summary>
/// Serializes the character + ledger for export or backup. JSON is complete and
/// re-importable later; Markdown is a human-readable build log you could share.
/// </summary>
public static class CharacterExporter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(Character character, IReadOnlyList<LedgerEntry> ledger) =>
        JsonSerializer.Serialize(new CharacterExport(DateTimeOffset.UtcNow, character, ledger), Json);

    public static string ToMarkdown(Character c, IReadOnlyList<LedgerEntry> ledger)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# D2Companion build log");
        sb.AppendLine();

        var name = string.IsNullOrWhiteSpace(c.Name) ? "(unnamed)" : c.Name;
        sb.AppendLine($"**{name}** — {c.Class} · {c.Mode}{Season(c.PatchOrSeason)}  ");
        if (!string.IsNullOrWhiteSpace(c.BuildGoal))
            sb.AppendLine($"Goal: {c.BuildGoal}  ");
        sb.AppendLine($"Progress: {c.Difficulty} · {Or(c.Act, "—")} · Level {c.Level}  ");
        sb.AppendLine($"Attributes: STR {c.Attributes.Strength} / DEX {c.Attributes.Dexterity} / " +
                      $"VIT {c.Attributes.Vitality} / ENE {c.Attributes.Energy}");
        sb.AppendLine();

        if (c.Skills.Count > 0)
        {
            sb.AppendLine("## Skills");
            foreach (var s in c.Skills)
                sb.AppendLine($"- {s.Skill} ({s.Tree}): {s.Points}");
            sb.AppendLine();
        }

        if (c.Gear.Count > 0)
        {
            sb.AppendLine("## Gear");
            foreach (var g in c.Gear)
                sb.AppendLine($"- **{g.Slot}:** {g.Item}{(string.IsNullOrWhiteSpace(g.Notes) ? "" : $" — {g.Notes}")}");
            sb.AppendLine();
        }

        if (c.Reminders.Count > 0)
        {
            sb.AppendLine("## Reminders");
            foreach (var r in c.Reminders)
                sb.AppendLine($"- {r}");
            sb.AppendLine();
        }

        sb.AppendLine("## Decision log");
        // Ledger arrives newest-first; read it oldest-first so it tells the story in order.
        var ordered = ledger.OrderBy(e => e.Id).ToList();
        var index = 1;
        foreach (var e in ordered)
        {
            var who = e.Source == DecisionSource.Claude ? "Claude" : "you";
            sb.AppendLine($"{index}. `{e.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm}` ({who}) {e.Details}");
            if (!string.IsNullOrWhiteSpace(e.Rationale))
                sb.AppendLine($"   - _{e.Rationale}_");
            index++;
        }
        sb.AppendLine();
        sb.AppendLine($"_Exported {DateTimeOffset.Now:yyyy-MM-dd HH:mm}._");

        return sb.ToString();
    }

    private static string Season(string patch) => string.IsNullOrWhiteSpace(patch) ? "" : $" · {patch}";
    private static string Or(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
