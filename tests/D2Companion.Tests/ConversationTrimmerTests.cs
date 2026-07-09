using Anthropic.Models.Messages;
using D2Companion.Brain;
using Xunit;

namespace D2Companion.Tests;

public sealed class ConversationTrimmerTests
{
    [Fact]
    public void UnderTheCap_NothingHappens()
    {
        var messages = Turns(5); // 5 turns × 4 messages = 20

        Assert.Equal(0, ConversationTrimmer.Trim(messages, maxMessages: 60));
        Assert.Equal(20, messages.Count);
    }

    [Fact]
    public void OverTheCap_TrimsToRoughlyHalf_WithABridgeNote()
    {
        var messages = Turns(20); // 80 messages

        var removed = ConversationTrimmer.Trim(messages, maxMessages: 60);

        Assert.True(removed > 0);
        Assert.True(messages.Count <= 31, $"still {messages.Count} messages"); // ~30 target + bridge
        Assert.True(messages[0].Content.TryPickString(out var first));
        Assert.Equal(ConversationTrimmer.BridgeNote, first);
    }

    [Fact]
    public void NeverCutsBetweenToolUseAndToolResult()
    {
        var messages = Turns(20);

        ConversationTrimmer.Trim(messages, maxMessages: 60);

        // The first real message after the bridge must be a player turn, not a stray
        // tool_result belonging to a trimmed assistant message.
        Assert.True(messages[1].Content.TryPickString(out var text));
        Assert.StartsWith("turn", text);
    }

    [Fact]
    public void RepeatedTrims_DontStackBridgeNotes()
    {
        var messages = Turns(20);
        ConversationTrimmer.Trim(messages, maxMessages: 60);

        // Keep playing: add more turns until the cap trips again.
        AddTurns(messages, 15);
        ConversationTrimmer.Trim(messages, maxMessages: 60);

        var bridges = messages.Count(m =>
            m.Content.TryPickString(out var t) && t == ConversationTrimmer.BridgeNote);
        Assert.Equal(1, bridges);
    }

    [Fact]
    public void OneGiantTurn_IsLeftAlone()
    {
        // A single player turn followed by a long tool-call chain — no safe boundary.
        var messages = new List<MessageParam> { PlayerText("turn 0") };
        for (var i = 0; i < 30; i++)
        {
            messages.Add(AssistantWithToolUse($"tu-{i}"));
            messages.Add(ToolResultMessage($"tu-{i}"));
        }

        Assert.Equal(0, ConversationTrimmer.Trim(messages, maxMessages: 20));
    }

    // --- helpers: a realistic voice turn is user → assistant(tool_use) → results → assistant ---

    private static List<MessageParam> Turns(int count)
    {
        var messages = new List<MessageParam>();
        AddTurns(messages, count);
        return messages;
    }

    private static void AddTurns(List<MessageParam> messages, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var n = messages.Count;
            messages.Add(PlayerText($"turn {n}"));
            messages.Add(AssistantWithToolUse($"tu-{n}"));
            messages.Add(ToolResultMessage($"tu-{n}"));
            messages.Add(AssistantText("done"));
        }
    }

    private static MessageParam PlayerText(string text) =>
        new() { Role = Role.User, Content = text };

    private static MessageParam AssistantText(string text) =>
        new()
        {
            Role = Role.Assistant,
            Content = new List<ContentBlockParam> { new TextBlockParam { Text = text } },
        };

    private static MessageParam AssistantWithToolUse(string id) =>
        new()
        {
            Role = Role.Assistant,
            Content = new List<ContentBlockParam>
            {
                new TextBlockParam { Text = "noting that" },
                new ToolUseBlockParam { ID = id, Name = "set_level", Input = new Dictionary<string, System.Text.Json.JsonElement>() },
            },
        };

    private static MessageParam ToolResultMessage(string id) =>
        new()
        {
            Role = Role.User,
            Content = new List<ContentBlockParam>
            {
                new ToolResultBlockParam { ToolUseID = id, Content = "Recorded." },
            },
        };
}
