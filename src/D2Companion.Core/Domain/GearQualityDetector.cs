using System.Text.RegularExpressions;

namespace D2Companion.Core.Domain;

/// <summary>
/// Works out an item's quality for display. An explicitly recorded quality always wins;
/// otherwise the item text is sniffed — first for a runeword (a parenthesized rune recipe
/// like "Spirit (Tal Thul Ort Amn)", the format the app documents for gear names), then
/// for quality words in the name or notes.
/// </summary>
public static class GearQualityDetector
{
    private static readonly HashSet<string> Runes = new(StringComparer.OrdinalIgnoreCase)
    {
        "El", "Eld", "Tir", "Nef", "Eth", "Ith", "Tal", "Ral", "Ort", "Thul", "Amn",
        "Sol", "Shael", "Dol", "Hel", "Io", "Lum", "Ko", "Fal", "Lem", "Pul", "Um",
        "Mal", "Ist", "Gul", "Vex", "Ohm", "Lo", "Sur", "Ber", "Jah", "Cham", "Zod",
    };

    private static readonly Regex Parenthesized = new(@"\(([^)]+)\)", RegexOptions.Compiled);

    // Ordered by specificity so e.g. "crafted rare" reads as Crafted.
    private static readonly (string Word, GearQuality Quality)[] Keywords =
    [
        ("runeword", GearQuality.Runeword),
        ("crafted", GearQuality.Crafted),
        ("unique", GearQuality.Unique),
        ("set", GearQuality.Set),
        ("rare", GearQuality.Rare),
        ("magic", GearQuality.Magic),
    ];

    public static GearQuality Detect(GearItem item) => Detect(item.Item, item.Notes, item.Quality);

    public static GearQuality Detect(string itemName, string notes, GearQuality explicitQuality)
    {
        if (explicitQuality != GearQuality.Unknown)
            return explicitQuality;

        if (LooksLikeRuneRecipe(itemName))
            return GearQuality.Runeword;

        var text = $"{itemName} {notes}";
        foreach (var (word, quality) in Keywords)
        {
            if (Regex.IsMatch(text, $@"\b{word}\b", RegexOptions.IgnoreCase))
                return quality;
        }
        return GearQuality.Unknown;
    }

    /// <summary>True when any parenthesized group is two-plus runes and nothing else.</summary>
    private static bool LooksLikeRuneRecipe(string itemName)
    {
        foreach (Match match in Parenthesized.Matches(itemName ?? ""))
        {
            var tokens = match.Groups[1].Value
                .Split([' ', '+', ',', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length >= 2 && tokens.All(Runes.Contains))
                return true;
        }
        return false;
    }
}
