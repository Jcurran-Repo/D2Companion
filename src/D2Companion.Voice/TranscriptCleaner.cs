namespace D2Companion.Voice;

/// <summary>
/// The speech-input "limits" you asked for: drop standalone filler words and ignore
/// near-empty input, so an "uh" or a throat-clear doesn't count as a turn. Deliberately
/// conservative — only tokens that are <em>entirely</em> filler are removed; ambiguous
/// words like "like" are kept, since they're often meaningful.
/// </summary>
public static class TranscriptCleaner
{
    private static readonly HashSet<string> Fillers = new(StringComparer.OrdinalIgnoreCase)
    {
        "um", "umm", "uh", "uhh", "uhm", "erm", "er", "ah", "hmm", "mm", "mhm", "huh",
    };

    private static readonly char[] Trim = { ',', '.', '!', '?', ';', ':' };

    /// <summary>
    /// Returns the cleaned transcript, or <c>null</c> if what's left is below
    /// <paramref name="minWords"/> real words (nothing worth sending to the brain).
    /// </summary>
    public static string? Clean(string? raw, int minWords = 1)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var tokens = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            if (!Fillers.Contains(token.Trim(Trim)))
                kept.Add(token);
        }

        if (kept.Count < minWords)
            return null;

        var cleaned = string.Join(' ', kept).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}
