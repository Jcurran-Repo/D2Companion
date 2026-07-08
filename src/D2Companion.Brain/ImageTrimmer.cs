using Anthropic.Models.Messages;

namespace D2Companion.Brain;

/// <summary>
/// Keeps conversation history from bloating with stale screenshots. Every image in
/// history is re-sent to the API on every subsequent turn (~1.8k input tokens each,
/// forever), while Claude only ever acts on the recent ones — so all but the newest
/// few are replaced with a short text placeholder. Only user-side content is touched
/// (pasted screenshots and view_screenshot tool results); assistant blocks are never
/// modified, so thinking-block replay stays valid.
/// </summary>
public static class ImageTrimmer
{
    public const string TrimmedNote =
        "[An older screenshot was removed from the conversation to save tokens. " +
        "Ask the player for a fresh one if you need eyes again.]";

    /// <summary>
    /// Replaces all but the newest <paramref name="keepLatest"/> images with placeholders,
    /// editing <paramref name="messages"/> in place. Returns how many were trimmed.
    /// </summary>
    public static int Trim(List<MessageParam> messages, int keepLatest)
    {
        var seen = 0;
        var trimmed = 0;

        // Newest-first so the most recent images fill the quota and older ones get cut.
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (!messages[i].Content.TryPickContentBlockParams(out var blocks) || blocks is null)
                continue;

            List<ContentBlockParam>? rebuilt = null;
            for (var b = blocks.Count - 1; b >= 0; b--)
            {
                var block = blocks[b];

                if (block.TryPickImage(out ImageBlockParam? _))
                {
                    if (++seen <= keepLatest) continue;
                    rebuilt ??= new List<ContentBlockParam>(blocks);
                    rebuilt[b] = new TextBlockParam { Text = TrimmedNote };
                    trimmed++;
                }
                else if (block.TryPickToolResult(out ToolResultBlockParam? toolResult) &&
                         ContainsImage(toolResult))
                {
                    if (++seen <= keepLatest) continue;
                    rebuilt ??= new List<ContentBlockParam>(blocks);
                    rebuilt[b] = new ToolResultBlockParam { ToolUseID = toolResult!.ToolUseID, Content = TrimmedNote };
                    trimmed++;
                }
            }

            if (rebuilt is not null)
                messages[i] = new MessageParam { Role = messages[i].Role, Content = rebuilt };
        }

        return trimmed;
    }

    private static bool ContainsImage(ToolResultBlockParam? toolResult)
    {
        if (toolResult?.Content is not { } content || !content.TryPickBlocks(out var blocks) || blocks is null)
            return false;
        foreach (var block in blocks)
        {
            if (block.TryPickImageBlockParam(out ImageBlockParam? _))
                return true;
        }
        return false;
    }
}
