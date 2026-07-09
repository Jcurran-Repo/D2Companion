using D2Companion.Brain;
using D2Companion.Core;
using D2Companion.Core.Persistence;
using Xunit;

namespace D2Companion.Tests;

public sealed class ModelCatalogTests
{
    private static CharacterService NewService() =>
        new(new SqliteCharacterStore(Path.Combine(Path.GetTempPath(), $"d2c-{Guid.NewGuid():N}.db")));

    [Fact]
    public void Catalog_HasTheDefault_AndSaneCostMeters()
    {
        Assert.Contains(ModelCatalog.All, m => m.Id == ModelCatalog.DefaultId);
        Assert.All(ModelCatalog.All, m =>
        {
            Assert.InRange(m.CostDots, 1, ModelChoice.MaxDots);
            Assert.Equal(ModelChoice.MaxDots, m.Dots.Length);
            Assert.False(string.IsNullOrWhiteSpace(m.Price));
            Assert.False(string.IsNullOrWhiteSpace(m.Blurb));
        });
        // Distinct ids, and the meter tracks the price ordering (cheapest first in the list).
        Assert.Equal(ModelCatalog.All.Count, ModelCatalog.All.Select(m => m.Id).Distinct().Count());
        Assert.Equal(ModelCatalog.All.OrderBy(m => m.CostDots).Select(m => m.Id), ModelCatalog.All.Select(m => m.Id));
    }

    [Fact]
    public void ById_MatchesCaseInsensitive_AndFallsBackToDefaultShape()
    {
        Assert.Equal("claude-haiku-4-5", ModelCatalog.ById("Claude-Haiku-4-5").Id);
        Assert.Equal(ModelCatalog.DefaultId, ModelCatalog.ById("some-future-model").Id);
        Assert.Equal(ModelCatalog.DefaultId, ModelCatalog.ById(null).Id);
    }

    [Fact]
    public void Dots_RenderCopilotStyle()
    {
        var haiku = ModelCatalog.ById("claude-haiku-4-5");
        Assert.Equal("●○○○○", haiku.Dots);

        var fable = ModelCatalog.ById("claude-fable-5");
        Assert.Equal("●●●●●", fable.Dots);
    }

    [Fact]
    public void RequestShape_OmitsEffortAndThinking_ForHaiku()
    {
        // Haiku 4.5 rejects the effort parameter and adaptive thinking with a 400 —
        // the request must simply omit them.
        var brain = new D2Brain(NewService(),
            new BrainOptions { ApiKey = "test-key", Model = "claude-haiku-4-5" });

        var request = brain.BuildParams();

        Assert.Equal("claude-haiku-4-5", request.Model);
        Assert.Null(request.Thinking);
        Assert.Null(request.OutputConfig);
        Assert.NotNull(request.CacheControl); // caching stays on for every model
    }

    [Fact]
    public void RequestShape_KeepsEffortAndThinking_ForOpus()
    {
        var brain = new D2Brain(NewService(),
            new BrainOptions { ApiKey = "test-key", Model = "claude-opus-4-8" });

        var request = brain.BuildParams();

        Assert.NotNull(request.Thinking);
        Assert.NotNull(request.OutputConfig);
    }
}
