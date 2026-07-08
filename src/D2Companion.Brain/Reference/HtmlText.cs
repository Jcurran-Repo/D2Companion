using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace D2Companion.Brain.Reference;

/// <summary>
/// Turns a fetched reference page into readable text and pulls the slice most relevant to a
/// topic. Deliberately dependency-free (regex, not a full HTML parser) and pure, so it's
/// unit-testable without hitting the network.
/// </summary>
public static class HtmlText
{
    /// <summary>Strips scripts/styles/tags from HTML and returns clean, line-broken text.</summary>
    public static string Extract(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        var text = Regex.Replace(html, "<script.*?</script>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<style.*?</style>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        // Turn block-level closers into line breaks so paragraphs survive.
        text = Regex.Replace(text, "<(br|/p|/div|/li|/h[1-6]|/tr)[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);

        var lines = text
            .Split('\n')
            .Select(line => Regex.Replace(line, "[ \t\f\v]+", " ").Trim())
            .Where(line => line.Length > 0);

        return string.Join("\n", lines);
    }

    /// <summary>Returns the paragraphs most relevant to <paramref name="topic"/>, up to a char budget.</summary>
    public static string Relevant(string? text, string? topic, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var keywords = Tokenize(topic);
        var paragraphs = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (keywords.Count == 0)
            return Truncate(text, maxChars);

        var scored = paragraphs
            .Select(p => (Paragraph: p, Score: Score(p, keywords)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToList();

        if (scored.Count == 0)
            return Truncate(text, maxChars);

        var builder = new StringBuilder();
        foreach (var (paragraph, _) in scored)
        {
            if (builder.Length + paragraph.Length + 1 > maxChars)
                break;
            builder.AppendLine(paragraph);
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? Truncate(scored[0].Paragraph, maxChars) : result;
    }

    private static int Score(string paragraph, IReadOnlyCollection<string> keywords)
    {
        var words = Tokenize(paragraph);
        return keywords.Sum(keyword => words.Count(w => w == keyword));
    }

    private static List<string> Tokenize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : Regex.Split(value.ToLowerInvariant(), "[^a-z0-9]+").Where(w => w.Length > 2).ToList();

    private static string Truncate(string value, int maxChars) =>
        value.Length <= maxChars ? value : value[..maxChars].TrimEnd() + "…";
}
