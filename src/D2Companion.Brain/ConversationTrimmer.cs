using Anthropic.Models.Messages;

namespace D2Companion.Brain;

/// <summary>
/// Expires old conversation turns. The ledger and character sheet are the durable memory —
/// every decision is already recorded and get_character_state serves it on demand — so the
/// chat history only needs to carry recent conversational flow, not the whole session.
/// Trims are hysteretic (cut to half the cap once the cap is hit) so the cache-invalidating
/// prefix change happens once every several turns, not every turn; cuts land only on
/// player-turn boundaries so no tool_use is ever separated from its tool_result.
/// </summary>
public static class ConversationTrimmer
{
    public const string BridgeNote =
        "[Older conversation was trimmed to save tokens. Nothing is lost: the character sheet, " +
        "ledger, runs, and deaths are all recorded — call get_character_state to re-orient.]";

    /// <summary>
    /// Trims <paramref name="messages"/> down to roughly half of <paramref name="maxMessages"/>
    /// once it exceeds it, inserting a bridge note where history was cut. Returns the number
    /// of messages removed.
    /// </summary>
    public static int Trim(List<MessageParam> messages, int maxMessages)
    {
        maxMessages = Math.Max(8, maxMessages);
        if (messages.Count <= maxMessages) return 0;

        // Keep the longest suffix that fits the post-trim target and starts on a player turn.
        var target = Math.Max(4, maxMessages / 2);
        var cut = -1;
        for (var i = Math.Max(1, messages.Count - target); i < messages.Count; i++)
        {
            if (IsPlayerTurnStart(messages[i]))
            {
                cut = i;
                break;
            }
        }
        if (cut < 0) return 0; // one enormous turn — nothing safe to cut

        // Any previous bridge note sits inside the removed range, so bridges never stack.
        messages.RemoveRange(0, cut);
        messages.Insert(0, new MessageParam { Role = Role.User, Content = BridgeNote });
        return cut;
    }

    /// <summary>A message that starts a player turn: spoken/typed text, or a screenshot
    /// turn (image + caption). Tool-result messages are user-role too but belong to the
    /// PREVIOUS assistant message — cutting there would orphan its tool calls.</summary>
    private static bool IsPlayerTurnStart(MessageParam message)
    {
        if (!message.Role.Equals(Role.User))
            return false;
        if (message.Content.TryPickString(out var text))
            return !string.IsNullOrEmpty(text);
        if (!message.Content.TryPickContentBlockParams(out var blocks) || blocks is null)
            return false;

        foreach (var block in blocks)
        {
            if (block.TryPickToolResult(out ToolResultBlockParam? _))
                return false;
        }
        return true;
    }
}
