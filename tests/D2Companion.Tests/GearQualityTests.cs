using System.Text.Json;
using D2Companion.Brain;
using D2Companion.Core;
using D2Companion.Core.Domain;
using D2Companion.Core.Ledger;
using D2Companion.Core.Persistence;
using Xunit;

namespace D2Companion.Tests;

public sealed class GearQualityTests
{
    private static SqliteCharacterStore NewStore() =>
        new(Path.Combine(Path.GetTempPath(), $"d2c-{Guid.NewGuid():N}.db"));

    [Theory]
    [InlineData("Spirit (Tal Thul Ort Amn)", "", GearQuality.Runeword)]
    [InlineData("Leaf (Tir + Ral)", "", GearQuality.Runeword)]
    [InlineData("Harlequin Crest", "unique — the classic Shako", GearQuality.Unique)]
    [InlineData("Sigon's Guard", "part of the set", GearQuality.Set)]
    [InlineData("Ring", "rare, dual leech", GearQuality.Rare)]
    [InlineData("Sapphire Ring", "magic", GearQuality.Magic)]
    [InlineData("Plain longsword", "", GearQuality.Unknown)]
    [InlineData("Ring (dual leech)", "", GearQuality.Unknown)] // parens but not a rune recipe
    [InlineData("Socketed helm", "", GearQuality.Unknown)]     // "set" inside a word doesn't count
    public void Detect_SniffsQualityFromText(string item, string notes, GearQuality expected) =>
        Assert.Equal(expected, GearQualityDetector.Detect(item, notes, GearQuality.Unknown));

    [Fact]
    public void Detect_ExplicitQualityWins()
    {
        // Text says runeword, but the recorded quality is authoritative.
        Assert.Equal(GearQuality.Unique,
            GearQualityDetector.Detect("Spirit (Tal Thul Ort Amn)", "", GearQuality.Unique));
    }

    [Fact]
    public void NoteGear_PersistsQuality()
    {
        var store = NewStore();
        var service = new CharacterService(store);

        service.NoteGear("Helm", "Harlequin Crest", DecisionSource.Claude, quality: GearQuality.Unique);

        var reloaded = new CharacterService(store).Current;
        Assert.Equal(GearQuality.Unique, Assert.Single(reloaded.Gear).Quality);
        Assert.Contains("(Unique)", store.LoadLedger()[0].Details);
    }

    [Fact]
    public void NoteGear_SameItemWithoutQuality_KeepsRecordedQuality()
    {
        var service = new CharacterService(NewStore());
        service.NoteGear("Helm", "Harlequin Crest", DecisionSource.Claude, quality: GearQuality.Unique);

        // Re-noting the same item (say, just updating notes) must not wipe the quality.
        service.NoteGear("Helm", "Harlequin Crest", DecisionSource.Claude, "ptopaz'd");

        Assert.Equal(GearQuality.Unique, Assert.Single(service.Current.Gear).Quality);
    }

    [Fact]
    public void NoteGear_UnchangedGear_LogsNothing()
    {
        var store = NewStore();
        var service = new CharacterService(store);
        service.NoteGear("Helm", "Harlequin Crest", DecisionSource.Claude, quality: GearQuality.Unique);

        service.NoteGear("Helm", "Harlequin Crest", DecisionSource.Claude, quality: GearQuality.Unique);

        Assert.Single(store.LoadLedger());
    }

    [Fact]
    public void Tool_NoteGear_AcceptsQuality()
    {
        var service = new CharacterService(NewStore());

        D2Tools.Execute(service, "note_gear", JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"slot":"Weapon","item":"Oculus","quality":"Unique"}""")!);

        Assert.Equal(GearQuality.Unique, Assert.Single(service.Current.Gear).Quality);
    }
}
