namespace D2Companion.Brain;

/// <summary>
/// One selectable brain model: identity, a Copilot-style cost meter (filled dots out of
/// <see cref="MaxDots"/>), list pricing, and what the model can take in a request —
/// BuildParams shapes each request from these flags so every catalog entry works as-is.
/// </summary>
public sealed record ModelChoice(
    string Id,
    string DisplayName,
    int CostDots,
    string Price,
    string Blurb,
    bool SupportsEffort,
    bool SupportsAdaptiveThinking)
{
    public const int MaxDots = 5;

    /// <summary>The cost meter, e.g. "●●●○○".</summary>
    public string Dots => new string('●', CostDots) + new string('○', MaxDots - CostDots);
}

/// <summary>
/// The models the app offers, curated to ones with strong vision — screenshot reading is
/// this companion's eyes, so text-only or vision-weak models don't belong here.
/// </summary>
public static class ModelCatalog
{
    public const string DefaultId = "claude-opus-4-8";

    public static readonly IReadOnlyList<ModelChoice> All =
    [
        new("claude-haiku-4-5", "Haiku 4.5", CostDots: 1, Price: "$1 / $5 per M tokens",
            Blurb: "Fastest and cheapest. Snappy calls, lighter reasoning, standard-res vision.",
            SupportsEffort: false, SupportsAdaptiveThinking: false),

        new("claude-sonnet-5", "Sonnet 5", CostDots: 2, Price: "$3 / $15 per M tokens",
            Blurb: "The sweet spot — near-Opus judgment with sharp high-res screenshot reading.",
            SupportsEffort: true, SupportsAdaptiveThinking: true),

        new(DefaultId, "Opus 4.8", CostDots: 3, Price: "$5 / $25 per M tokens",
            Blurb: "The default. Excellent build judgment, high-res vision, great narration.",
            SupportsEffort: true, SupportsAdaptiveThinking: true),

        new("claude-fable-5", "Fable 5", CostDots: 5, Price: "$10 / $50 per M tokens",
            Blurb: "Anthropic's most capable. Overkill for Blood Moor — glorious for hardcore Hell.",
            SupportsEffort: true, SupportsAdaptiveThinking: true),
    ];

    /// <summary>Finds a catalog entry by model id; unknown ids get the default's request
    /// shape so a hand-edited settings.json still produces a valid request.</summary>
    public static ModelChoice ById(string? id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? All.First(m => m.Id == DefaultId);
}
