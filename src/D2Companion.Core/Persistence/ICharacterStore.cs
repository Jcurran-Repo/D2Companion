using D2Companion.Core.Domain;
using D2Companion.Core.Ledger;

namespace D2Companion.Core.Persistence;

/// <summary>
/// Persistence seam for the character aggregate and its decision ledger. Kept an
/// interface so the SQLite implementation can be swapped (in-memory for tests,
/// something else later) without touching the service or UI.
/// </summary>
public interface ICharacterStore
{
    /// <summary>Loads the saved character, or a fresh <see cref="Character"/> if none exists.</summary>
    Character LoadCharacter();

    /// <summary>Saves (upserts) the single character aggregate.</summary>
    void SaveCharacter(Character character);

    /// <summary>Appends a ledger entry and populates its <see cref="LedgerEntry.Id"/>.</summary>
    void AppendLedger(LedgerEntry entry);

    /// <summary>Returns the most recent ledger entries, newest first.</summary>
    IReadOnlyList<LedgerEntry> LoadLedger(int max = 200);

    /// <summary>Deletes the saved character and the entire ledger (a fresh start).</summary>
    void ClearAll();
}
