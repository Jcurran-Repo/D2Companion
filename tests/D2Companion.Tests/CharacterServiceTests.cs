using D2Companion.Core;
using D2Companion.Core.Domain;
using D2Companion.Core.Ledger;
using D2Companion.Core.Persistence;
using Xunit;

namespace D2Companion.Tests;

public sealed class CharacterServiceTests
{
    private static SqliteCharacterStore NewStore() =>
        new(Path.Combine(Path.GetTempPath(), $"d2c-{Guid.NewGuid():N}.db"));

    [Fact]
    public void FreshStore_ReturnsUnsetCharacter()
    {
        var character = NewStore().LoadCharacter();

        Assert.Equal(CharacterClass.Unset, character.Class);
        Assert.Equal(GameMode.Unset, character.Mode);
        Assert.Equal(1, character.Level);
        Assert.Empty(character.Skills);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAggregate()
    {
        var store = NewStore();
        var service = new CharacterService(store);

        service.SetClass(CharacterClass.Sorceress, DecisionSource.Claude);
        service.SetLevel(12, DecisionSource.Claude);
        service.AssignSkillPoint("Frozen Orb", "Cold", DecisionSource.Claude, delta: 3);

        // A brand-new service over the same store must see the persisted state.
        var reloaded = new CharacterService(store).Current;

        Assert.Equal(CharacterClass.Sorceress, reloaded.Class);
        Assert.Equal(12, reloaded.Level);
        var skill = Assert.Single(reloaded.Skills);
        Assert.Equal("Frozen Orb", skill.Skill);
        Assert.Equal(3, skill.Points);
    }

    [Fact]
    public void SetClass_LogsLedgerEntry_WithClaudeSource()
    {
        var store = NewStore();
        var service = new CharacterService(store);

        service.SetClass(CharacterClass.Paladin, DecisionSource.Claude, "Hammerdin plan.");

        var entry = Assert.Single(store.LoadLedger());
        Assert.Equal("set_class", entry.Action);
        Assert.Equal(DecisionSource.Claude, entry.Source);
        Assert.Equal("Hammerdin plan.", entry.Rationale);
    }

    [Fact]
    public void SettingSameValueTwice_LogsOnlyOnce()
    {
        var store = NewStore();
        var service = new CharacterService(store);

        service.SetLevel(10, DecisionSource.Claude);
        service.SetLevel(10, DecisionSource.Claude); // no-op, must not log

        Assert.Single(store.LoadLedger());
    }

    [Fact]
    public void AssignSkillPoint_AccumulatesPoints()
    {
        var service = new CharacterService(NewStore());

        service.AssignSkillPoint("Blizzard", "Cold", DecisionSource.Claude);
        service.AssignSkillPoint("Blizzard", "Cold", DecisionSource.Claude, delta: 4);

        var skill = Assert.Single(service.Current.Skills);
        Assert.Equal(5, skill.Points);
    }

    [Fact]
    public void SetSkill_ToZero_RemovesSkill()
    {
        var service = new CharacterService(NewStore());
        service.SetSkill("Fire Bolt", "Fire", 3, DecisionSource.Claude);

        service.SetSkill("Fire Bolt", "Fire", 0, DecisionSource.Manual);

        Assert.Empty(service.Current.Skills);
    }

    [Fact]
    public void LoadLedger_ReturnsNewestFirst()
    {
        var store = NewStore();
        var service = new CharacterService(store);

        service.SetLevel(2, DecisionSource.Claude);
        service.SetLevel(3, DecisionSource.Claude);

        var ledger = store.LoadLedger();
        Assert.Equal("Level → 3", ledger[0].Details);
        Assert.Equal("Level → 2", ledger[1].Details);
    }

    [Fact]
    public void Changed_FiresWithEntry_OnCommit()
    {
        var service = new CharacterService(NewStore());
        CharacterChangedEventArgs? captured = null;
        service.Changed += (_, e) => captured = e;

        service.SetName("Wraithsong", DecisionSource.Manual);

        Assert.NotNull(captured);
        Assert.Equal("set_name", captured!.Entry.Action);
    }
}
