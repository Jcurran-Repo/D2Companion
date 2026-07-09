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
    private readonly Func<CapturedImage?>? _screenshotSource;

    public D2Brain(CharacterService service, BrainOptions options, ReferenceLibrary? reference = null,
        Func<CapturedImage?>? screenshotSource = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _reference = reference;
        _screenshotSource = screenshotSource;
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
    public Task<string> SendAsync(string playerText) =>
        RunTurnAsync(new MessageParam { Role = Role.User, Content = playerText });

    /// <summary>
    /// Sends a screenshot (with accompanying text) as the player's turn — Claude's eyes on
    /// the game. Same tool loop as a text turn.
    /// </summary>
    public Task<string> SendAsync(string playerText, byte[] imageBytes, string imageMediaType) =>
        RunTurnAsync(new MessageParam
        {
            Role = Role.User,
            Content = BuildImageContent(playerText, imageBytes, imageMediaType),
        });

    /// <summary>
    /// Runs one tool call, converting any failure into an is_error tool_result instead of
    /// letting it escape. This invariant matters: Claude's tool_use is already in history,
    /// and a tool_use without a matching tool_result makes the API reject EVERY subsequent
    /// request — one flaky clipboard read would otherwise brick the whole conversation.
    /// </summary>
    internal async Task<ToolResultBlockParam> RunToolAsync(ToolUseBlock toolUse)
    {
        try
        {
            // view_screenshot returns image content, not a string — handle it here.
            if (toolUse.Name == "view_screenshot" && _screenshotSource is not null)
                return new ToolResultBlockParam { ToolUseID = toolUse.ID, Content = BuildScreenshotResult(_screenshotSource()) };

            // lookup_reference is the one async tool (it fetches web pages); everything
            // else is a synchronous state mutation through the service.
            var result = toolUse.Name == "lookup_reference" && _reference is not null
                ? await _reference.LookupAsync(ReadTopic(toolUse.Input))
                : D2Tools.Execute(_service, toolUse.Name, toolUse.Input);
            return new ToolResultBlockParam { ToolUseID = toolUse.ID, Content = result };
        }
        catch (Exception ex)
        {
            return new ToolResultBlockParam
            {
                ToolUseID = toolUse.ID,
                Content = ToolFailureMessage(toolUse.Name, ex),
                IsError = true,
            };
        }
    }

    /// <summary>What Claude is told when a tool throws — enough to explain and move on.</summary>
    public static string ToolFailureMessage(string toolName, Exception ex) =>
        $"The {toolName} tool failed: {ex.Message}. Tell the player briefly and carry on; they can retry if it matters.";

    /// <summary>Builds the tool_result content for view_screenshot: the clipboard image when
    /// there is one, or a nudge to take a screenshot when there isn't. Public for tests.</summary>
    public static ToolResultBlockParamContent BuildScreenshotResult(CapturedImage? capture)
    {
        if (capture is null)
            return "No screenshot found — the clipboard has no image. Ask the player to hit " +
                   "PrtScn (or Win+Shift+S) and say the word again.";

        return new List<Block>
        {
            new ImageBlockParam
            {
                Source = new Base64ImageSource
                {
                    Data = Convert.ToBase64String(capture.Data),
                    MediaType = capture.MediaType,
                },
            },
            new TextBlockParam { Text = "The player's screenshot, fresh off their clipboard." },
        };
    }

    /// <summary>Builds the image-plus-text content for a screenshot turn (image first, per
    /// the vision guidance). Public so the payload shape stays testable without an API call.</summary>
    public static List<ContentBlockParam> BuildImageContent(string text, byte[] imageBytes, string mediaType) =>
    [
        new ImageBlockParam
        {
            Source = new Base64ImageSource
            {
                Data = Convert.ToBase64String(imageBytes),
                MediaType = mediaType,
            },
        },
        new TextBlockParam { Text = text },
    ];

    private async Task<string> RunTurnAsync(MessageParam userMessage)
    {
        _messages.Add(userMessage);
        ImageTrimmer.Trim(_messages, _options.MaxRetainedImages);

        // The whole turn's speech, not just the last response's: Claude usually says the
        // decision ALONGSIDE its tool calls ("I'd equip the leather armor — recording it")
        // and only wraps up briefly after the results. Returning only the final response
        // silently swallowed exactly the sentences the player needed to hear.
        var spoken = new StringBuilder();

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
                {
                    assistantContent.Add(new TextBlockParam { Text = text.Text });
                    AppendSpoken(spoken, text.Text);
                }
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
            {
                // Safety classifiers (notably on Fable 5) can decline with an empty reply;
                // and a thinking-only response can legitimately carry no text. Either way,
                // never hand the voice layer an empty string.
                if (spoken.Length > 0)
                    return spoken.ToString();
                return response.StopReason == "refusal"
                    ? "I can't help with that one — let's get back to the game."
                    : "Say that again?";
            }

            // Run each tool against the service and hand the results back to Claude.
            var toolResults = new List<ContentBlockParam>();
            foreach (var toolUse in toolUses)
                toolResults.Add(await RunToolAsync(toolUse));
            _messages.Add(new MessageParam { Role = Role.User, Content = toolResults });
        }

        return spoken.Length > 0
            ? spoken.ToString()
            : "(I took too many bookkeeping steps on that one — ask me again.)";
    }

    /// <summary>Accumulates one turn's speech across tool-loop iterations. Public for tests.</summary>
    public static void AppendSpoken(StringBuilder spoken, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (spoken.Length > 0) spoken.Append(' ');
        spoken.Append(text.Trim());
    }

    internal MessageCreateParams BuildParams()
    {
        // Request shape follows the selected model's capabilities: Haiku 4.5 rejects both
        // the effort parameter and adaptive thinking with a 400, so those are omitted there.
        var model = ModelCatalog.ById(_options.Model);

        return new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            // Two cache breakpoints. The system one pins tools + system prompt (stable for the
            // whole session). The top-level one auto-places on the newest message block, so each
            // request re-reads the entire prior conversation at ~10% price instead of
            // re-processing it at full rate — without it, turn N pays full price for all N-1
            // earlier turns and the session cost curve goes quadratic.
            // 1-hour TTL, not the 5-minute default: this is a game companion — the player
            // regularly goes quiet for a boss fight or a farming stretch, and every gap past
            // the TTL forces a full-price re-write of the whole history. 1h writes cost 2×
            // (vs 1.25×) once per increment but survive the entire session's silences.
            CacheControl = new CacheControlEphemeral { Ttl = Ttl.Ttl1h },
            System = new List<TextBlockParam>
            {
                new() { Text = SystemPrompt.Text, CacheControl = new CacheControlEphemeral { Ttl = Ttl.Ttl1h } },
            },
            Tools = _tools,
            Thinking = model.SupportsAdaptiveThinking ? (ThinkingConfigParam)new ThinkingConfigAdaptive() : null,
            OutputConfig = model.SupportsEffort ? new OutputConfig { Effort = _effort } : null,
            Messages = _messages,
        };
    }

    private static string ReadTopic(IReadOnlyDictionary<string, JsonElement> input) =>
        input.TryGetValue("topic", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

}
