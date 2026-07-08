using D2Companion.Core.Domain;
using D2Companion.Core.Export;
using D2Companion.Core.Ledger;
using D2Companion.Core.Persistence;

namespace D2Companion.Core;

/// <summary>Raised after any committed change, carrying the ledger entry that recorded it.</summary>
public sealed class CharacterChangedEventArgs(LedgerEntry entry) : EventArgs
{
    public LedgerEntry Entry { get; } = entry;
}

/// <summary>
/// The one place the character can be mutated. Every method persists the aggregate,
/// appends a ledger entry, and raises <see cref="Changed"/>. Both the in-app Claude
/// (via tools, later) and the player's manual edits go through here — so the "Claude
/// decides" path and the "hand-correct" path share one audited pipeline.
/// </summary>
public sealed class CharacterService
{
    private readonly ICharacterStore _store;

    public CharacterService(ICharacterStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Current = _store.LoadCharacter();
    }

    /// <summary>The live character aggregate. Treat as read-only; mutate via the methods below.</summary>
    public Character Current { get; }

    public event EventHandler<CharacterChangedEventArgs>? Changed;

    /// <summary>The most recent ledger entries, newest first. Read-through to the store.</summary>
    public IReadOnlyList<LedgerEntry> RecentLedger(int max = 200) => _store.LoadLedger(max);

    /// <summary>The entire ledger, newest first — used for export.</summary>
    public IReadOnlyList<LedgerEntry> FullLedger() => _store.LoadLedger(int.MaxValue);

    // --- Identity & plan -------------------------------------------------

    public void SetName(string name, DecisionSource source, string? rationale = null)
    {
        name = (name ?? "").Trim();
        if (Current.Name == name) return;
        Current.Name = name;
        Commit(source, "set_name", $"Name → \"{name}\"", rationale);
    }

    public void SetClass(CharacterClass value, DecisionSource source, string? rationale = null)
    {
        if (Current.Class == value) return;
        Current.Class = value;
        Commit(source, "set_class", $"Class → {value}", rationale);
    }

    public void SetMode(GameMode value, DecisionSource source, string? rationale = null)
    {
        if (Current.Mode == value) return;
        Current.Mode = value;
        Commit(source, "set_mode", $"Mode → {value}", rationale);
    }

    public void SetPatchOrSeason(string value, DecisionSource source, string? rationale = null)
    {
        value = (value ?? "").Trim();
        if (Current.PatchOrSeason == value) return;
        Current.PatchOrSeason = value;
        Commit(source, "set_patch_or_season", $"Patch/season → \"{value}\"", rationale);
    }

    public void SetBuildGoal(string value, DecisionSource source, string? rationale = null)
    {
        value = (value ?? "").Trim();
        if (Current.BuildGoal == value) return;
        Current.BuildGoal = value;
        Commit(source, "set_build_goal", $"Build goal → \"{value}\"", rationale);
    }

    // --- Progress --------------------------------------------------------

    public void SetLevel(int level, DecisionSource source, string? rationale = null)
    {
        level = Math.Clamp(level, 1, 99);
        if (Current.Level == level) return;
        Current.Level = level;
        Commit(source, "set_level", $"Level → {level}", rationale);
    }

    public void SetDifficulty(Difficulty value, DecisionSource source, string? rationale = null)
    {
        if (Current.Difficulty == value) return;
        Current.Difficulty = value;
        Commit(source, "set_difficulty", $"Difficulty → {value}", rationale);
    }

    public void SetAct(string act, DecisionSource source, string? rationale = null)
    {
        act = (act ?? "").Trim();
        if (Current.Act == act) return;
        Current.Act = act;
        Commit(source, "set_act", $"Act → \"{act}\"", rationale);
    }

    /// <summary>Convenience for setting difficulty and act together (one ledger entry).</summary>
    public void SetProgress(Difficulty difficulty, string act, DecisionSource source, string? rationale = null)
    {
        act = (act ?? "").Trim();
        if (Current.Difficulty == difficulty && Current.Act == act) return;
        Current.Difficulty = difficulty;
        Current.Act = act;
        Commit(source, "set_progress", $"Progress → {difficulty}, \"{act}\"", rationale);
    }

    // --- Attributes ------------------------------------------------------

    public void SetAttributes(int strength, int dexterity, int vitality, int energy,
        DecisionSource source, string? rationale = null)
    {
        var a = Current.Attributes;
        if (a.Strength == strength && a.Dexterity == dexterity && a.Vitality == vitality && a.Energy == energy)
            return;
        a.Strength = Math.Max(0, strength);
        a.Dexterity = Math.Max(0, dexterity);
        a.Vitality = Math.Max(0, vitality);
        a.Energy = Math.Max(0, energy);
        Commit(source, "set_attributes",
            $"Attributes → STR {a.Strength} / DEX {a.Dexterity} / VIT {a.Vitality} / ENE {a.Energy}", rationale);
    }

    // --- Skills ----------------------------------------------------------

    /// <summary>Adds <paramref name="delta"/> points (usually 1) to a skill, creating it if new.</summary>
    public void AssignSkillPoint(string skill, string tree, DecisionSource source, int delta = 1, string? rationale = null)
    {
        skill = (skill ?? "").Trim();
        if (skill.Length == 0 || delta == 0) return;

        var existing = FindSkill(skill);
        var points = Math.Max(0, (existing?.Points ?? 0) + delta);
        SetSkill(skill, tree, points, source, rationale ?? $"{(delta > 0 ? "+" : "")}{delta} point(s)");
    }

    /// <summary>Sets a skill to an absolute point total. Zero (or less) removes it.</summary>
    public void SetSkill(string skill, string tree, int points, DecisionSource source, string? rationale = null)
    {
        skill = (skill ?? "").Trim();
        tree = (tree ?? "").Trim();
        if (skill.Length == 0) return;

        var existing = FindSkill(skill);
        points = Math.Max(0, points);

        if (points == 0)
        {
            if (existing is null) return;
            Current.Skills.Remove(existing);
            Commit(source, "remove_skill", $"Removed skill \"{skill}\"", rationale);
            return;
        }

        if (existing is null)
        {
            Current.Skills.Add(new SkillAllocation { Skill = skill, Tree = tree, Points = points });
            Commit(source, "set_skill", $"{skill} ({tree}) → {points}", rationale);
            return;
        }

        if (existing.Points == points && existing.Tree == tree) return;
        existing.Tree = tree;
        existing.Points = points;
        Commit(source, "set_skill", $"{skill} ({tree}) → {points}", rationale);
    }

    // --- Gear ------------------------------------------------------------

    /// <summary>Records gear for a slot, replacing whatever was in that slot.</summary>
    public void NoteGear(string slot, string item, DecisionSource source, string notes = "", string? rationale = null)
    {
        slot = (slot ?? "").Trim();
        item = (item ?? "").Trim();
        notes = (notes ?? "").Trim();
        if (slot.Length == 0) return;

        var existing = FindGear(slot);
        if (existing is not null && existing.Item == item && existing.Notes == notes) return;

        if (existing is null)
            Current.Gear.Add(new GearItem { Slot = slot, Item = item, Notes = notes });
        else
        {
            existing.Item = item;
            existing.Notes = notes;
        }
        Commit(source, "note_gear", $"{slot}: \"{item}\"", rationale);
    }

    public void RemoveGear(string slot, DecisionSource source, string? rationale = null)
    {
        slot = (slot ?? "").Trim();
        var existing = FindGear(slot);
        if (existing is null) return;
        Current.Gear.Remove(existing);
        Commit(source, "remove_gear", $"Removed gear in \"{slot}\"", rationale);
    }

    // --- Reminders -------------------------------------------------------

    public void AddReminder(string text, DecisionSource source, string? rationale = null)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0 || Current.Reminders.Contains(text)) return;
        Current.Reminders.Add(text);
        Commit(source, "add_reminder", $"Reminder: \"{text}\"", rationale);
    }

    public void RemoveReminder(string text, DecisionSource source, string? rationale = null)
    {
        text = (text ?? "").Trim();
        if (!Current.Reminders.Remove(text)) return;
        Commit(source, "remove_reminder", $"Removed reminder: \"{text}\"", rationale);
    }

    // --- Runs & deaths -----------------------------------------------------

    /// <summary>Records one completed farming run and returns the run number for that
    /// target (1-based, case-insensitive match). Returns 0 when no target was given.</summary>
    public int LogRun(string target, DecisionSource source, string note = "", string? rationale = null)
    {
        target = (target ?? "").Trim();
        note = (note ?? "").Trim();
        if (target.Length == 0) return 0;

        Current.Runs.Add(new RunRecord { TimestampUtc = DateTimeOffset.UtcNow, Target = target, Note = note });
        var count = Current.Runs.Count(r => string.Equals(r.Target, target, StringComparison.OrdinalIgnoreCase));
        Commit(source, "log_run",
            $"Run #{count} — {target}{(note.Length == 0 ? "" : $" · {note}")}", rationale);
        return count;
    }

    /// <summary>Records a death with a snapshot of where the character stood.</summary>
    public void LogDeath(string cause, DecisionSource source, string? rationale = null)
    {
        cause = (cause ?? "").Trim();
        if (cause.Length == 0) cause = "unknown cause";

        Current.Deaths.Add(new DeathRecord
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Cause = cause,
            Level = Current.Level,
            Difficulty = Current.Difficulty,
            Act = Current.Act,
        });
        var where = string.IsNullOrWhiteSpace(Current.Act) ? $"{Current.Difficulty}" : $"{Current.Difficulty}, {Current.Act}";
        Commit(source, "log_death",
            $"Death #{Current.Deaths.Count} — level {Current.Level} ({where}): {cause}", rationale);
    }

    // --- Lifecycle -------------------------------------------------------

    /// <summary>Clears the character and the entire ledger for a fresh run, then logs the
    /// reset as the first entry of the new ledger.</summary>
    public void NewCharacter(DecisionSource source, string? rationale = null)
    {
        ResetCurrent();
        _store.ClearAll();

        var entry = new LedgerEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Source = source,
            Action = "new_character",
            Details = "Started a new character",
            Rationale = string.IsNullOrWhiteSpace(rationale) ? null : rationale.Trim(),
        };
        _store.SaveCharacter(Current);
        _store.AppendLedger(entry);
        Changed?.Invoke(this, new CharacterChangedEventArgs(entry));
    }

    /// <summary>Replaces the current character and ledger with an imported export.</summary>
    public void ImportCharacter(CharacterExport export, DecisionSource source)
    {
        ArgumentNullException.ThrowIfNull(export);
        if (export.Character is null)
            throw new InvalidOperationException("The import has no character.");

        CopyInto(Current, export.Character);
        _store.ClearAll();
        _store.SaveCharacter(Current);

        // Re-lay the imported log oldest-first so ids keep their original order.
        var imported = export.Ledger ?? Array.Empty<LedgerEntry>();
        foreach (var e in imported.OrderBy(e => e.Id))
        {
            _store.AppendLedger(new LedgerEntry
            {
                TimestampUtc = e.TimestampUtc,
                Source = e.Source,
                Action = e.Action,
                Details = e.Details,
                Rationale = e.Rationale,
            });
        }

        var note = new LedgerEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Source = source,
            Action = "import",
            Details = $"Imported character ({imported.Count} prior log entries)",
        };
        _store.AppendLedger(note);
        Changed?.Invoke(this, new CharacterChangedEventArgs(note));
    }

    private static void CopyInto(Character target, Character source)
    {
        target.Name = source.Name;
        target.Class = source.Class;
        target.Mode = source.Mode;
        target.PatchOrSeason = source.PatchOrSeason;
        target.BuildGoal = source.BuildGoal;
        target.Difficulty = source.Difficulty;
        target.Act = source.Act;
        target.Level = source.Level;
        target.Attributes = new AttributePoints
        {
            Strength = source.Attributes.Strength,
            Dexterity = source.Attributes.Dexterity,
            Vitality = source.Attributes.Vitality,
            Energy = source.Attributes.Energy,
        };
        target.Skills.Clear();
        target.Skills.AddRange(source.Skills.Select(s => new SkillAllocation { Skill = s.Skill, Tree = s.Tree, Points = s.Points }));
        target.Gear.Clear();
        target.Gear.AddRange(source.Gear.Select(g => new GearItem { Slot = g.Slot, Item = g.Item, Notes = g.Notes }));
        target.Reminders.Clear();
        target.Reminders.AddRange(source.Reminders);
        target.Runs.Clear();
        target.Runs.AddRange(source.Runs.Select(r => new RunRecord { TimestampUtc = r.TimestampUtc, Target = r.Target, Note = r.Note }));
        target.Deaths.Clear();
        target.Deaths.AddRange(source.Deaths.Select(d => new DeathRecord
        {
            TimestampUtc = d.TimestampUtc,
            Cause = d.Cause,
            Level = d.Level,
            Difficulty = d.Difficulty,
            Act = d.Act,
        }));
    }

    private void ResetCurrent()
    {
        Current.Name = "";
        Current.Class = CharacterClass.Unset;
        Current.Mode = GameMode.Unset;
        Current.PatchOrSeason = "";
        Current.BuildGoal = "";
        Current.Difficulty = Difficulty.Unset;
        Current.Act = "";
        Current.Level = 1;
        Current.Attributes = new AttributePoints();
        Current.Skills.Clear();
        Current.Gear.Clear();
        Current.Reminders.Clear();
        Current.Runs.Clear();
        Current.Deaths.Clear();
    }

    // --- Internals -------------------------------------------------------

    private SkillAllocation? FindSkill(string skill) =>
        Current.Skills.FirstOrDefault(s => string.Equals(s.Skill, skill, StringComparison.OrdinalIgnoreCase));

    private GearItem? FindGear(string slot) =>
        Current.Gear.FirstOrDefault(g => string.Equals(g.Slot, slot, StringComparison.OrdinalIgnoreCase));

    private void Commit(DecisionSource source, string action, string details, string? rationale)
    {
        _store.SaveCharacter(Current);
        var entry = new LedgerEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Source = source,
            Action = action,
            Details = details,
            Rationale = string.IsNullOrWhiteSpace(rationale) ? null : rationale.Trim(),
        };
        _store.AppendLedger(entry);
        Changed?.Invoke(this, new CharacterChangedEventArgs(entry));
    }
}
