using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using D2Companion.Brain.Reference;
using D2Companion.Core;

namespace D2Companion.Brain;

/// <summary>
/// The Claude brain. Holds the running conversation, runs a manual tool-use loop against
/// the Messages API, and dispatches Claude's tool calls to <see cref="CharacterService"/>.
/// We drive the loop by hand (rather than the SDK's auto tool-runner) so later phases can
/// gate, log, or throttle each call. Returns Claude's spoken reply for the voice layer.
/// </summary>
public sealed class D2Brain
{
    private readonly CharacterService _service;
    private readonly BrainOptions _options;
    private readonly AnthropicClient _client;
    private readonly List<MessageParam> _messages = new();
    private readonly List<ToolUnion> _tools = new();
    private readonly Effort _effort;
    private readonly ReferenceLibrary? _reference;

    public D2Brain(CharacterService service, BrainOptions options, ReferenceLibrary? reference = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _reference = reference;
        _client = new AnthropicClient { ApiKey = options.ApiKey };

        foreach (var tool in D2Tools.Definitions)
            _tools.Add(tool); // implicit Tool -> ToolUnion

        _effort = options.Effort.ToLowerInvariant() switch
        {
            "medium" => Effort.Medium,
            "high" => Effort.High,
            "max" => Effort.Max,
            _ => Effort.Low,
        };
    }

    /// <summary>Clears the conversation for a fresh session (the character state is untouched).</summary>
    public void Reset() => _messages.Clear();

    /// <summary>
    /// Sends the player's message and runs the tool loop until Claude produces a spoken reply.
    /// Claude may call tools mid-turn to record decisions; each one flows straight into the
    /// character state and the ledger before the reply comes back.
    /// </summary>
    public async Task<string> SendAsync(string playerText)
    {
        _messages.Add(new MessageParam { Role = Role.User, Content = playerText });

        for (var iteration = 0; iteration < _options.MaxToolIterations; iteration++)
        {
            var response = await _client.Messages.Create(BuildParams());

            // Echo the assistant turn back into history: text, thinking (with signature, so it
            // replays cleanly on the same model), and any tool_use blocks.
            var assistantContent = new List<ContentBlockParam>();
            var toolUses = new List<ToolUseBlock>();
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out TextBlock? text))
                    assistantContent.Add(new TextBlockParam { Text = text.Text });
                else if (block.TryPickThinking(out ThinkingBlock? thinking))
                    assistantContent.Add(new ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? redacted))
                    assistantContent.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
                else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
                {
                    assistantContent.Add(new ToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
                    toolUses.Add(toolUse);
                }
            }
            _messages.Add(new MessageParam { Role = Role.Assistant, Content = assistantContent });

            if (response.StopReason != "tool_use")
                return CollectText(response.Content);

            // Run each tool against the service and hand the results back to Claude.
            var toolResults = new List<ContentBlockParam>();
            foreach (var toolUse in toolUses)
            {
                // lookup_reference is the one async tool (it fetches web pages); everything
                // else is a synchronous state mutation through the service.
                var result = toolUse.Name == "lookup_reference" && _reference is not null
                    ? await _reference.LookupAsync(ReadTopic(toolUse.Input))
                    : D2Tools.Execute(_service, toolUse.Name, toolUse.Input);
                toolResults.Add(new ToolResultBlockParam { ToolUseID = toolUse.ID, Content = result });
            }
            _messages.Add(new MessageParam { Role = Role.User, Content = toolResults });
        }

        return "(I took too many bookkeeping steps on that one — ask me again.)";
    }

    private MessageCreateParams BuildParams() => new()
    {
        Model = _options.Model,
        MaxTokens = _options.MaxTokens,
        // System prompt is stable, so cache it — each turn then only pays for the new
        // message plus the reply. Tools render before system and cache along with it.
        System = new List<TextBlockParam>
        {
            new() { Text = SystemPrompt.Text, CacheControl = new CacheControlEphemeral() },
        },
        Tools = _tools,
        Thinking = new ThinkingConfigAdaptive(),
        OutputConfig = new OutputConfig { Effort = _effort },
        Messages = _messages,
    };

    private static string ReadTopic(IReadOnlyDictionary<string, JsonElement> input) =>
        input.TryGetValue("topic", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string CollectText(IReadOnlyList<ContentBlock> content)
    {
        var builder = new StringBuilder();
        foreach (var block in content)
        {
            if (block.TryPickText(out TextBlock? text) && !string.IsNullOrWhiteSpace(text.Text))
            {
                if (builder.Length > 0) builder.Append(' ');
                builder.Append(text.Text);
            }
        }
        return builder.ToString().Trim();
    }
}
