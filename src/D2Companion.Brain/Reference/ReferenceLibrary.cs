using System.Text;

namespace D2Companion.Brain.Reference;

/// <summary>
/// Backs the lookup_reference tool: fetches the configured seed pages (once, then cached),
/// extracts readable text, and returns the slice most relevant to the topic Claude asked
/// about. Claude uses this only to verify — its own D2 knowledge is the first resort.
/// </summary>
public sealed class ReferenceLibrary : IDisposable
{
    private readonly ReferenceOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ReferenceLibrary(ReferenceOptions options, HttpClient? http = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("D2Companion/1.0");
    }

    public async Task<string> LookupAsync(string topic, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return "No topic was provided to look up.";
        if (_options.Sources.Count == 0)
            return "No reference sites are configured — use your own D2 knowledge.";

        var perSource = Math.Max(400, _options.MaxCharsReturned / _options.Sources.Count);
        var builder = new StringBuilder();

        foreach (var source in _options.Sources)
        {
            string text;
            try
            {
                text = await GetTextAsync(source.Url, cancellationToken);
            }
            catch (Exception ex)
            {
                builder.AppendLine($"[{source.Name}: couldn't fetch — {ex.Message}]");
                continue;
            }

            var excerpt = HtmlText.Relevant(text, topic, perSource);
            if (excerpt.Length == 0)
                continue;

            builder.AppendLine($"From {source.Name} ({source.Url}):");
            builder.AppendLine(excerpt);
            builder.AppendLine();

            if (builder.Length >= _options.MaxCharsReturned)
                break;
        }

        var result = builder.ToString().Trim();
        return result.Length == 0
            ? "No strong match in the reference sites — use your own D2 knowledge."
            : result;
    }

    private async Task<string> GetTextAsync(string url, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(url, out var cached))
                return cached;

            var html = await _http.GetStringAsync(url, cancellationToken);
            var text = HtmlText.Extract(html);
            _cache[url] = text;
            return text;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
        _gate.Dispose();
    }
}
