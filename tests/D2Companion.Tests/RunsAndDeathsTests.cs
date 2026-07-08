using System.Text.Json;
using D2Companion.Brain;
using D2Companion.Core;
using D2Companion.Core.Domain;
using D2Companion.Core.Export;
using D2Companion.Core.Ledger;
using D2Companion.Core.Persistence;
using Xunit;

namespace D2Companion.Tests;

public sealed class RunsAndDeathsTests
{
    private static SqliteCharacterStore NewStore() =>
        new(Path.Combine(Path.GetTempPath(), $"d2c-{Guid.NewGuid():N}.db"));

    [Fact]
    public void LogRun_CountsPerTarget_CaseInsensitive()
    {
        var service = new CharacterService(NewStore());

        Assert.Equal(1, service.LogRun("Mephisto", DecisionSource.Claude));
        Assert.Equal(2, service.LogRun("mephisto", DecisionSource.Claude, "Shako!"));
        Assert.Equal(1, service.LogRun("Baal", DecisionSource.Claude));

        Assert.Equal(3, service.Current.Runs.Count);
        Assert.Equal("Shako!", service.Current.Runs[1].Note);
    }

    [Fact]
    public void LogRun_WithoutTarget_RecordsNothing()
    {
        var store = NewStore();
        var service = new CharacterService(store);

        Assert.Equal(0, service.LogRun("  ", DecisionSource.Claude));

        Assert.Empty(service.Current.Runs);
        Assert.Empty(store.LoadLedger());
    }

    [Fact]
    public void LogRun_AppendsLedgerEntry_WithDrop()
    {
        var store = NewStore();
        var service = new CharacterService(store);

        service.LogRun("Mephisto", DecisionSource.Claude, "Shako!", "Big find.");

        var entry = Assert.Single(store.LoadLedger());
        Assert.Equal("log_run", entry.Action);
        Assert.Contains("Run #1 — Mephisto", entry.Details);
        Assert.Contains("Shako!", entry.Details);
    }

    [Fact]
    public void LogDeath_SnapshotsWhereTheCharacterStood()
    {
        var store = NewStore();
        var service = new CharacterService(store);
        service.SetLevel(42, DecisionSource.Claude);
        service.SetProgress(Difficulty.Nightmare, "Act 5", DecisionSource.Claude);

        service.LogDeath("Frenzytaur pack in the Worldstone Keep", DecisionSource.Claude);

        var death = Assert.Single(service.Current.Deaths);
        Assert.Equal(42, death.Level);
        Assert.Equal(Difficulty.Nightmare, death.Difficulty);
        Assert.Equal("Act 5", death.Act);
        Assert.Contains("Death #1", store.LoadLedger()[0].Details);
    }

    [Fact]
    public void RunsAndDeaths_RoundTripThroughStore()
    {
        var store = NewStore();
        var service = new CharacterService(store);
        service.LogRun("Pindleskin", DecisionSource.Claude, "Occy");
        service.LogDeath("lag spike", DecisionSource.Claude);

        var reloaded = new CharacterService(store).Current;

        Assert.Equal("Occy", Assert.Single(reloaded.Runs).Note);
        Assert.Equal("lag spike", Assert.Single(reloaded.Deaths).Cause);
    }

    [Fact]
    public void NewCharacter_ClearsRunsAndDeaths()
    {
        var service = new CharacterService(NewStore());
        service.LogRun("Mephisto", DecisionSource.Claude);
        service.LogDeath("gloams", DecisionSource.Claude);

        service.NewCharacter(DecisionSource.Manual);

        Assert.Empty(service.Current.Runs);
        Assert.Empty(service.Current.Deaths);
    }

    [Fact]
    public void Tools_DispatchLogRunAndLogDeath()
    {
        var service = new CharacterService(NewStore());

        var runResult = D2Tools.Execute(service, "log_run", Input("""{"target":"Mephisto","drop":"Shako!"}"""));
        var deathResult = D2Tools.Execute(service, "log_death", Input("""{"cause":"Council fire"}"""));

        Assert.Equal("Recorded — run #1 for Mephisto.", runResult);
        Assert.Equal("Recorded.", deathResult);
        Assert.Single(service.Current.Runs);
        Assert.Single(service.Current.Deaths);
    }

    [Fact]
    public void Markdown_IncludesRunsAndDeaths()
    {
        var service = new CharacterService(NewStore());
        service.LogRun("Mephisto", DecisionSource.Claude);
        service.LogRun("Mephisto", DecisionSource.Claude, "Shako!");
        service.LogDeath("Duriel", DecisionSource.Claude);

        var markdown = CharacterExporter.ToMarkdown(service.Current, service.FullLedger());

        Assert.Contains("## Farming runs", markdown);
        Assert.Contains("**Mephisto:** 2 run(s)", markdown);
        Assert.Contains("Shako!", markdown);
        Assert.Contains("## Deaths", markdown);
        Assert.Contains("Duriel", markdown);
    }

    private static IReadOnlyDictionary<string, JsonElement> Input(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}
