namespace D2Companion.Core.Domain;

/// <summary>
/// The seven Diablo II: Resurrected classes. <see cref="Unset"/> is the starting
/// state — the in-app Claude decides the class and records it, rather than the
/// player choosing up front.
/// </summary>
public enum CharacterClass
{
    Unset = 0,
    Amazon,
    Assassin,
    Barbarian,
    Druid,
    Necromancer,
    Paladin,
    Sorceress,
}

/// <summary>Softcore vs Hardcore. Decided in-app, so it starts <see cref="Unset"/>.</summary>
public enum GameMode
{
    Unset = 0,
    Softcore,
    Hardcore,
}

/// <summary>The three difficulty tiers a character progresses through.</summary>
public enum Difficulty
{
    Unset = 0,
    Normal,
    Nightmare,
    Hell,
}

/// <summary>
/// Item quality tiers, matching the game's item-name colors (magic blue, rare yellow,
/// set green, unique gold, …). <see cref="Unknown"/> means "not stated" — the UI then
/// falls back to sniffing the item text via GearQualityDetector.
/// </summary>
public enum GearQuality
{
    Unknown = 0,
    Normal,
    Magic,
    Rare,
    Set,
    Unique,
    Runeword,
    Crafted,
}
