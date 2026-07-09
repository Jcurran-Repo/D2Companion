using System.Text.Json;
using D2Companion.Brain;
using D2Companion.Core;
using D2Companion.Core.Domain;
using D2Companion.Core.Ledger;
using D2Companion.Core.Persistence;
using Xunit;

namespace D2Companion.Tests;

public sealed class TokenEfficiencyTests
{
    private static CharacterService NewService() =>
        new(new SqliteCharacterStore(Path.Combine(Path.GetTempPath(), $"d2c-{Guid.NewGuid():N}.db")));

    [Fact]
    public void StateView_SummarizesRuns_InsteadOfListingEveryRecord()
    {
        var service = NewService();
        for (var i = 0; i < 40; i++)
            service.LogRun("Mephisto", DecisionSource.Claude, i == 39 ? "Shako!" : "");
        service.LogRun("Baal", DecisionSource.Claude);

        var view = D2Tools.BuildStateView(service.Current);

        Assert.Contains("\"total\":41", view);
        Assert.Contains("\"target\":\"Mephisto\",\"runs\":40", view);
        Assert.Contains("Shako!", view);
        Assert.DoesNotContain("TimestampUtc", view);          // no per-run records
        Assert.True(view.Length < 2000, $"state view ballooned to {view.Length} chars");
    }

    [Fact]
    public void StateView_IsCompact_AndKeepsTheCurrentBuildComplete()
    {
        var service = NewService();
        service.SetClass(CharacterClass.Sorceress, DecisionSource.Claude);
        service.SetSkill("Frozen Orb", "Cold", 5, DecisionSource.Claude);
        service.NoteGear("Helm", "Harlequin Crest", DecisionSource.Claude, quality: GearQuality.Unique);
        service.AddReminder("Save the socket quest", DecisionSource.Claude);

        var view = D2Tools.BuildStateView(service.Current);

        Assert.DoesNotContain("\n", view); // compact, not pretty-printed
        Assert.Contains("Sorceress", view);
        Assert.Contains("Frozen Orb", view);
        Assert.Contains("Harlequin Crest", view);
        Assert.Contains("Unique", view);
        Assert.Contains("Save the socket quest", view);
    }

    [Fact]
    public void StateView_CapsDeathsAtRecentThree()
    {
        var service = NewService();
        foreach (var cause in new[] { "one", "two", "three", "four", "five" })
            service.LogDeath(cause, DecisionSource.Claude);

        var view = D2Tools.BuildStateView(service.Current);

        Assert.Contains("\"total\":5", view);
        Assert.DoesNotContain("one", view.Substring(view.IndexOf("deaths")));
        Assert.Contains("five", view);
    }

    [Fact]
    public void RequestParams_CacheBothTheSystemPrefixAndTheConversation()
    {
        var brain = new D2Brain(NewService(), new BrainOptions { ApiKey = "test-key" });

        var request = brain.BuildParams();

        // Top-level auto-cache marks the newest message block, so history re-reads at
        // ~10% price instead of full reprocessing every turn. 1h TTL because game sessions
        // have quiet stretches well past the 5-minute default.
        Assert.NotNull(request.CacheControl);
        Assert.Contains("\"1h\"", JsonSerializer.Serialize(request.CacheControl));
        Assert.True(request.System!.TryPickTextBlockParams(out var system));
        Assert.NotNull(system![^1].CacheControl);
        Assert.Contains("\"1h\"", JsonSerializer.Serialize(system[^1].CacheControl));
    }
}
