namespace D2Companion.Core.Domain;

/// <summary>The four base attributes and how many points have been invested in each.</summary>
public sealed class AttributePoints
{
    public int Strength { get; set; }
    public int Dexterity { get; set; }
    public int Vitality { get; set; }
    public int Energy { get; set; }
}

/// <summary>A single skill and the number of hard points allocated to it.</summary>
public sealed class SkillAllocation
{
    /// <summary>Skill name, e.g. "Frozen Orb".</summary>
    public string Skill { get; set; } = "";

    /// <summary>Skill tree the skill belongs to, e.g. "Cold" or "Combat Masteries". Free text.</summary>
    public string Tree { get; set; } = "";

    /// <summary>Hard points spent on this skill.</summary>
    public int Points { get; set; }
}

/// <summary>A noted piece of gear in a given slot.</summary>
public sealed class GearItem
{
    /// <summary>Equipment slot, e.g. "Weapon", "Helm", "Ring 1". Free text.</summary>
    public string Slot { get; set; } = "";

    /// <summary>Item name / runeword, e.g. "Spirit (Tal Thul Ort Amn)".</summary>
    public string Item { get; set; } = "";

    /// <summary>Optional notes on why it's equipped or what to look for next.</summary>
    public string Notes { get; set; } = "";

    /// <summary>Item quality when stated; <see cref="GearQuality.Unknown"/> otherwise.</summary>
    public GearQuality Quality { get; set; } = GearQuality.Unknown;
}

/// <summary>One completed farming run (a Mephisto run, a Baal run, …).</summary>
public sealed class RunRecord
{
    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>What was being farmed, e.g. "Mephisto". Free text.</summary>
    public string Target { get; set; } = "";

    /// <summary>Notable drop(s) from the run; empty when it gave nothing worth naming.</summary>
    public string Note { get; set; } = "";
}

/// <summary>A death, with enough context snapshot to tell the story later.</summary>
public sealed class DeathRecord
{
    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>What killed the character, e.g. "Fire Enchanted boss pack in the Pit".</summary>
    public string Cause { get; set; } = "";

    public int Level { get; set; }
    public Difficulty Difficulty { get; set; }
    public string Act { get; set; } = "";
}

/// <summary>
/// The complete build state for one character. This is the single source of truth:
/// because the in-app Claude makes every build decision, tracking is just persisting
/// what Claude decided (plus any manual corrections). Serialized to JSON in one column.
/// </summary>
public sealed class Character
{
    public string Name { get; set; } = "";
    public CharacterClass Class { get; set; } = CharacterClass.Unset;
    public GameMode Mode { get; set; } = GameMode.Unset;

    /// <summary>Patch / ladder season, e.g. "2.8 Ladder S9". Free text.</summary>
    public string PatchOrSeason { get; set; } = "";

    /// <summary>The build Claude is steering toward, e.g. "Blizzard Sorceress".</summary>
    public string BuildGoal { get; set; } = "";

    public Difficulty Difficulty { get; set; } = Difficulty.Unset;

    /// <summary>Current act, e.g. "Act 2". Free text.</summary>
    public string Act { get; set; } = "";

    public int Level { get; set; } = 1;

    public AttributePoints Attributes { get; set; } = new();
    public List<SkillAllocation> Skills { get; set; } = new();
    public List<GearItem> Gear { get; set; } = new();
    public List<string> Reminders { get; set; } = new();
    public List<RunRecord> Runs { get; set; } = new();
    public List<DeathRecord> Deaths { get; set; } = new();
}
