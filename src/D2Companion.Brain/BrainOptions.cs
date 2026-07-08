namespace D2Companion.Brain;

/// <summary>Configuration for the Claude brain. The API key comes from user-secrets at runtime.</summary>
public sealed class BrainOptions
{
    public required string ApiKey { get; init; }

    /// <summary>Anthropic model id. Opus 4.8 by default — newest, our own key.</summary>
    public string Model { get; init; } = "claude-opus-4-8";

    /// <summary>Cap on a single spoken reply. Voice replies are short.</summary>
    public int MaxTokens { get; init; } = 1024;

    /// <summary>Safety cap on tool-loop round trips within one turn.</summary>
    public int MaxToolIterations { get; init; } = 8;

    /// <summary>Reasoning effort: low | medium | high | max. Low keeps voice replies snappy.</summary>
    public string Effort { get; init; } = "low";
}
