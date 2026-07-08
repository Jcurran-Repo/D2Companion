using D2Companion.Voice;
using Xunit;

namespace D2Companion.Tests;

public sealed class TranscriptCleanerTests
{
    [Fact]
    public void RemovesStandaloneFillers()
    {
        var cleaned = TranscriptCleaner.Clean("um put a point in uh Frozen Orb");
        Assert.Equal("put a point in Frozen Orb", cleaned);
    }

    [Fact]
    public void StripsFillerEvenWithTrailingPunctuation()
    {
        var cleaned = TranscriptCleaner.Clean("um, okay let's go");
        Assert.Equal("okay let's go", cleaned);
    }

    [Fact]
    public void KeepsAmbiguousWordsLikeLike()
    {
        // "like" is often meaningful, so it must survive the filter.
        var cleaned = TranscriptCleaner.Clean("I like lightning skills");
        Assert.Equal("I like lightning skills", cleaned);
    }

    [Fact]
    public void AllFiller_ReturnsNull()
    {
        Assert.Null(TranscriptCleaner.Clean("uh um hmm"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyOrWhitespace_ReturnsNull(string? raw)
    {
        Assert.Null(TranscriptCleaner.Clean(raw));
    }

    [Fact]
    public void MinWordsGate_RejectsTooShort()
    {
        Assert.Null(TranscriptCleaner.Clean("hello", minWords: 2));
        Assert.Equal("hello there", TranscriptCleaner.Clean("hello there", minWords: 2));
    }
}
