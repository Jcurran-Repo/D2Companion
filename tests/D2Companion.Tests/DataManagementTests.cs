using System.Text.Json;
using System.Text.Json.Serialization;
using D2Companion.Core;
using D2Companion.Core.Domain;
using D2Companion.Core.Export;
using D2Companion.Core.Ledger;
using D2Companion.Core.Persistence;
using Xunit;

namespace D2Companion.Tests;

public sealed class DataManagementTests
{
    private static SqliteCharacterStore NewStore() =>
        new(Path.Combine(Path.GetTempPath(), $"d2c-{Guid.NewGuid():N}.db"));

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void NewCharacter_ClearsCharacterAndLedger()
    {
        var store = NewStore();
        var service = new CharacterService(store);
        service.SetClass(CharacterClass.Sorceress, DecisionSource.Claude);
        service.SetLevel(20, DecisionSource.Claude);
        service.AssignSkillPoint("Blizzard", "Cold", DecisionSource.Claude);

        service.NewCharacter(DecisionSource.Manual);

        Assert.Equal(CharacterClass.Unset, service.Current.Class);
        Assert.Equal(1, service.Current.Level);
        Assert.Empty(service.Current.Skills);

        // The old ledger is gone; only the "new_character" entry remains.
        var ledger = store.LoadLedger();
        Assert.Single(ledger);
        Assert.Equal("new_character", ledger[0].Action);
    }

    [Fact]
    public void NewCharacter_PersistsAcrossReload()
    {
        var store = NewStore();
        var service = new CharacterService(store);
        service.SetClass(CharacterClass.Paladin, DecisionSource.Claude);

        service.NewCharacter(DecisionSource.Manual);

        var reloaded = new CharacterService(store).Current;
        Assert.Equal(CharacterClass.Unset, reloaded.Class);
    }

    [Fact]
    public void Export_ToJson_RoundTrips()
    {
        var service = new CharacterService(NewStore());
        service.SetName("Wraithsong", DecisionSource.Claude);
        service.SetClass(CharacterClass.Sorceress, DecisionSource.Claude);
        service.SetLevel(12, DecisionSource.Claude);

        var json = CharacterExporter.ToJson(service.Current, service.FullLedger());
        var export = JsonSerializer.Deserialize<CharacterExport>(json, Json);

        Assert.NotNull(export);
        Assert.Equal("Wraithsong", export!.Character.Name);
        Assert.Equal(CharacterClass.Sorceress, export.Character.Class);
        Assert.Equal(12, export.Character.Level);
        Assert.Equal(service.FullLedger().Count, export.Ledger.Count);
    }

    [Fact]
    public void Export_ToMarkdown_ContainsBuildAndLog()
    {
        var service = new CharacterService(NewStore());
        service.SetClass(CharacterClass.Sorceress, DecisionSource.Claude, "Blizzard sorc.");

        var markdown = CharacterExporter.ToMarkdown(service.Current, service.FullLedger());

        Assert.Contains("# D2Companion build log", markdown);
        Assert.Contains("Sorceress", markdown);
        Assert.Contains("## Decision log", markdown);
        Assert.Contains("Blizzard sorc.", markdown);
    }
}
