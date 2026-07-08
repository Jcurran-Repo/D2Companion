using D2Companion.Core.Ledger;

namespace D2Companion.App.ViewModels;

/// <summary>Read-only display row for one ledger entry in the UI.</summary>
public sealed class LedgerRow
{
    public LedgerRow(LedgerEntry entry)
    {
        Time = entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
        Source = entry.Source == DecisionSource.Claude ? "Claude" : "Manual";
        Details = entry.Details;
        Rationale = entry.Rationale ?? "";
    }

    public string Time { get; }
    public string Source { get; }
    public string Details { get; }
    public string Rationale { get; }
}
