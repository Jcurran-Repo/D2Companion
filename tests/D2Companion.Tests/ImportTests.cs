using D2Companion.Core;
using D2Companion.Core.Domain;
using D2Companion.Core.Export;
using D2Companion.Core.Ledger;
using D2Companion.Core.Persistence;
using Xunit;

namespace D2Companion.Tests;

public sealed class ImportTests
{
    private static SqliteCharacterStore NewStore() =>
        new(Path.Combine(Path.GetTempPath(), $"d2c-{Guid.NewGuid():N}.db"));

    [Fact]
    public void ExportThenImport_RoundTrips()
    {
        var source = new CharacterService(NewStore());
        source.SetName("Wraithsong", DecisionSource.Claude);
        source.SetClass(CharacterClass.Sorceress, DecisionSource.Claude);
        source.SetLevel(15, DecisionSource.Claude);
        source.AssignSkillPoint("Frozen Orb", "Cold", DecisionSource.Claude, delta: 5);

        var json = CharacterExporter.ToJson(source.Current, source.FullLedger());
        var export = CharacterImporter.FromJson(json);
        Assert.NotNull(export);

        var target = new CharacterService(NewStore());
        target.ImportCharacter(export!, DecisionSource.Manual);

        Assert.Equal("Wraithsong", target.Current.Name);
        Assert.Equal(CharacterClass.Sorceress, target.Current.Class);
        Assert.Equal(15, target.Current.Level);
        var skill = Assert.Single(target.Current.Skills);
        Assert.Equal(5, skill.Points);
    }

    [Fact]
    public void Import_ReplacesExistingCharacter_AndPersists()
    {
        var store = NewStore();
        var service = new CharacterService(store);
        service.SetClass(CharacterClass.Paladin, DecisionSource.Claude);

        var export = new CharacterExport(
            DateTimeOffset.UtcNow,
            new Character { Name = "Ravenmoon", Class = CharacterClass.Necromancer, Level = 30 },
            Array.Empty<LedgerEntry>());

        service.ImportCharacter(export, DecisionSource.Manual);

        Assert.Equal(CharacterClass.Necromancer, service.Current.Class);
        Assert.Equal("Ravenmoon", service.Current.Name);

        // Persisted: a fresh service over the same store sees the imported character.
        Assert.Equal(CharacterClass.Necromancer, new CharacterService(store).Current.Class);
    }

    [Fact]
    public void FromJson_Garbage_ReturnsNull()
    {
        Assert.Null(CharacterImporter.FromJson("not json at all"));
        Assert.Null(CharacterImporter.FromJson(""));
    }
}
