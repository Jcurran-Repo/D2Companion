using System.Text.Json;
using Anthropic.Models.Messages;
using D2Companion.Brain;
using D2Companion.Core;
using D2Companion.Core.Persistence;
using Xunit;

namespace D2Companion.Tests;

public sealed class ConversationResilienceTests
{
    private static readonly byte[] FakeJpeg = { 1, 2, 3 };

    // --- Screenshot trimming -------------------------------------------------

    [Fact]
    public void Trim_ReplacesOldImages_KeepsNewestOnes()
    {
        var messages = new List<MessageParam>
        {
            UserText("start a sorc"),
            ImageTurn("shot 1"), // oldest — should be trimmed
            UserText("level 5 now"),
            ImageTurn("shot 2"),
            ImageTurn("shot 3"),
        };

        var trimmed = ImageTrimmer.Trim(messages, keepLatest: 2);

        Assert.Equal(1, trimmed);
        Assert.Equal(0, CountImages(messages[1]));
        Assert.Contains(ImageTrimmer.TrimmedNote, Serialize(messages[1]));
        Assert.Equal(1, CountImages(messages[3]));
        Assert.Equal(1, CountImages(messages[4]));
    }

    [Fact]
    public void Trim_AlsoTrimsScreenshotToolResults()
    {
        var messages = new List<MessageParam>
        {
            ScreenshotToolResultTurn("tool-1"), // oldest
            ImageTurn("newer paste"),
            ScreenshotToolResultTurn("tool-2"),
        };

        var trimmed = ImageTrimmer.Trim(messages, keepLatest: 2);

        Assert.Equal(1, trimmed);
        var json = Serialize(messages[0]);
        Assert.Contains(ImageTrimmer.TrimmedNote, json);
        Assert.Contains("tool-1", json); // tool_use id must survive the swap
        Assert.DoesNotContain(Convert.ToBase64String(FakeJpeg), json);
    }

    [Fact]
    public void Trim_IsIdempotent_AndLeavesTextTurnsAlone()
    {
        var messages = new List<MessageParam>
        {
            UserText("hello"),
            ImageTurn("a"),
            ImageTurn("b"),
            ImageTurn("c"),
        };

        Assert.Equal(1, ImageTrimmer.Trim(messages, keepLatest: 2));
        Assert.Equal(0, ImageTrimmer.Trim(messages, keepLatest: 2)); // nothing left to do
        Assert.Contains("hello", Serialize(messages[0]));
    }

    [Fact]
    public void Trim_NeverTouchesAssistantMessages()
    {
        var assistant = new MessageParam
        {
            Role = Role.Assistant,
            Content = new List<ContentBlockParam>
            {
                new ThinkingBlockParam { Thinking = "hmm", Signature = "sig" },
                new TextBlockParam { Text = "Take the waypoint." },
            },
        };
        var messages = new List<MessageParam> { ImageTurn("old"), assistant, ImageTurn("new") };

        ImageTrimmer.Trim(messages, keepLatest: 1);

        Assert.Same(assistant, messages[1]); // untouched, byte-for-byte replay stays valid
    }

    // --- Tool-failure containment --------------------------------------------

    [Fact]
    public async Task FailingTool_BecomesErrorResult_NotAnException()
    {
        var brain = new D2Brain(
            NewService(),
            new BrainOptions { ApiKey = "test-key" },
            reference: null,
            screenshotSource: () => throw new InvalidOperationException("clipboard is busy"));

        var result = await brain.RunToolAsync(new ToolUseBlock
        {
            ID = "tu-1",
            Name = "view_screenshot",
            Input = new Dictionary<string, JsonElement>(),
            Caller = new DirectCaller(),
        });

        Assert.Equal("tu-1", result.ToolUseID);
        Assert.True(result.IsError);
        Assert.Contains("clipboard is busy", Serialize(result));
    }

    [Fact]
    public async Task HealthyTool_StillRunsNormally()
    {
        var brain = new D2Brain(NewService(), new BrainOptions { ApiKey = "test-key" });

        var result = await brain.RunToolAsync(new ToolUseBlock
        {
            ID = "tu-2",
            Name = "log_death",
            Input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                """{"cause":"gloams"}""")!,
            Caller = new DirectCaller(),
        });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("Recorded", Serialize(result));
    }

    // --- Spoken-reply accumulation --------------------------------------------

    [Fact]
    public void AppendSpoken_JoinsAcrossIterations_AndSkipsBlanks()
    {
        var spoken = new System.Text.StringBuilder();

        // Iteration 1: the decision, said alongside the tool calls.
        D2Brain.AppendSpoken(spoken, "I'd equip the leather armor — better defense.");
        D2Brain.AppendSpoken(spoken, null);
        D2Brain.AppendSpoken(spoken, "   ");
        // Final iteration: the wrap-up after tool results.
        D2Brain.AppendSpoken(spoken, "Noted. Keep clearing the Den.");

        Assert.Equal(
            "I'd equip the leather armor — better defense. Noted. Keep clearing the Den.",
            spoken.ToString());
    }

    [Fact]
    public void ToolFailureMessage_NamesTheToolAndReason()
    {
        var message = D2Brain.ToolFailureMessage("lookup_reference", new TimeoutException("no route"));

        Assert.Contains("lookup_reference", message);
        Assert.Contains("no route", message);
    }

    // --- helpers --------------------------------------------------------------

    private static CharacterService NewService() =>
        new(new SqliteCharacterStore(Path.Combine(Path.GetTempPath(), $"d2c-{Guid.NewGuid():N}.db")));

    private static MessageParam UserText(string text) =>
        new() { Role = Role.User, Content = text };

    private static MessageParam ImageTurn(string caption) =>
        new() { Role = Role.User, Content = D2Brain.BuildImageContent(caption, FakeJpeg, "image/jpeg") };

    private static MessageParam ScreenshotToolResultTurn(string toolUseId) =>
        new()
        {
            Role = Role.User,
            Content = new List<ContentBlockParam>
            {
                new ToolResultBlockParam
                {
                    ToolUseID = toolUseId,
                    Content = D2Brain.BuildScreenshotResult(new CapturedImage(FakeJpeg, "image/jpeg")),
                },
            },
        };

    private static int CountImages(MessageParam message)
    {
        var count = 0;
        if (!message.Content.TryPickContentBlockParams(out var blocks) || blocks is null)
            return 0;
        foreach (var block in blocks)
        {
            if (block.TryPickImage(out ImageBlockParam? _))
                count++;
        }
        return count;
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value);
}
