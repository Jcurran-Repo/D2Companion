using D2Companion.Brain.Reference;
using Xunit;

namespace D2Companion.Tests;

public sealed class HtmlTextTests
{
    [Fact]
    public void Extract_StripsTagsScriptsAndStyles()
    {
        const string html =
            "<html><head><style>.x{color:red}</style></head>" +
            "<body><h1>Sorceress</h1><script>evil()</script>" +
            "<p>Blizzard is a strong cold skill.</p></body></html>";

        var text = HtmlText.Extract(html);

        Assert.Contains("Sorceress", text);
        Assert.Contains("Blizzard is a strong cold skill.", text);
        Assert.DoesNotContain("evil()", text);
        Assert.DoesNotContain("color:red", text);
        Assert.DoesNotContain("<", text);
    }

    [Fact]
    public void Relevant_ReturnsTopicMatchingParagraph()
    {
        const string text =
            "Fire spells are hot.\n" +
            "Blizzard is a cold Sorceress skill with great area damage.\n" +
            "This paragraph is about something unrelated.";

        var result = HtmlText.Relevant(text, "blizzard cold sorceress", 500);

        Assert.Contains("Blizzard", result);
        Assert.DoesNotContain("unrelated", result);
    }

    [Fact]
    public void Relevant_NoMatch_FallsBackToText()
    {
        var result = HtmlText.Relevant("Alpha beta gamma. Delta epsilon.", "zzz nonexistent", 100);
        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void Extract_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Equal("", HtmlText.Extract(null));
        Assert.Equal("", HtmlText.Extract("   "));
    }
}
