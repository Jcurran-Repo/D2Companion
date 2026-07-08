namespace D2Companion.Brain.Reference;

/// <summary>A seed website Claude may consult via lookup_reference.</summary>
public sealed record ReferenceSource(string Name, string Url);

/// <summary>Configured reference sources and how much text a lookup may return.</summary>
public sealed class ReferenceOptions
{
    public IReadOnlyList<ReferenceSource> Sources { get; init; } = DefaultSources;

    /// <summary>Total character budget returned to Claude for one lookup.</summary>
    public int MaxCharsReturned { get; init; } = 1500;

    /// <summary>The out-of-the-box seed list. Editable via reference-sites.json.</summary>
    public static IReadOnlyList<ReferenceSource> DefaultSources { get; } = new[]
    {
        new ReferenceSource("Icy Veins — Diablo II", "https://www.icy-veins.com/d2/"),
    };
}
