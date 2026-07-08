using D2Companion.Core.Domain;

namespace D2Companion.App.ViewModels;

/// <summary>One farming target's tally for the Runs panel.</summary>
public sealed class RunSummaryRow
{
    public RunSummaryRow(string target, IReadOnlyList<RunRecord> runs)
    {
        Target = target;
        Count = runs.Count;
        LastDrop = runs.LastOrDefault(r => !string.IsNullOrWhiteSpace(r.Note))?.Note ?? "—";
    }

    public string Target { get; }
    public int Count { get; }
    public string LastDrop { get; }
}
