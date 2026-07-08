namespace D2Companion.Core.Ledger;

/// <summary>Who caused a change to the character state.</summary>
public enum DecisionSource
{
    /// <summary>The in-app Claude decided it (via a tool call, once the brain lands).</summary>
    Claude = 0,

    /// <summary>The player hand-corrected it in the character sheet.</summary>
    Manual = 1,
}

/// <summary>
/// An append-only record of one change to the character. The ledger is the project's
/// audit trail and the seed of a future history / undo view — every decision Claude
/// makes and every manual edit lands here.
/// </summary>
public sealed class LedgerEntry
{
    /// <summary>Auto-assigned by the store on append.</summary>
    public long Id { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    public DecisionSource Source { get; set; }

    /// <summary>Machine-ish action name, e.g. "set_level" or "assign_skill_point".</summary>
    public string Action { get; set; } = "";

    /// <summary>Human-readable summary of what changed, e.g. "Level → 12".</summary>
    public string Details { get; set; } = "";

    /// <summary>Claude's reasoning, when supplied. Null for most manual edits.</summary>
    public string? Rationale { get; set; }
}
