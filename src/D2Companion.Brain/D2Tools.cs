using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.Models.Messages;
using D2Companion.Core;
using D2Companion.Core.Domain;
using D2Companion.Core.Ledger;

namespace D2Companion.Brain;

/// <summary>
/// The tool surface Claude uses to record its decisions, plus the dispatcher that
/// runs them against <see cref="CharacterService"/>. Every mutation tool routes to
/// the same audited pipeline the manual UI uses — so a Claude tool call and a hand
/// edit are indistinguishable to the store and both land in the ledger.
/// </summary>
public static class D2Tools
{
    private static readonly string[] Classes =
        Enum.GetNames<CharacterClass>().Where(n => n != nameof(CharacterClass.Unset)).ToArray();

    private static readonly string[] Modes =
        Enum.GetNames<GameMode>().Where(n => n != nameof(GameMode.Unset)).ToArray();

    private static readonly string[] Difficulties =
        Enum.GetNames<Difficulty>().Where(n => n != nameof(Difficulty.Unset)).ToArray();

    private static readonly string[] GearQualities =
        Enum.GetNames<GearQuality>().Where(n => n != nameof(GearQuality.Unknown)).ToArray();

    private static readonly JsonSerializerOptions StateJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // Must be declared BEFORE Definitions: static fields initialize in textual order, and
    // Build() reads Rationale for every mutation tool. If it's declared later it's still a
    // default(JsonElement) at build time, which throws "Operation is not valid due to the
    // current state of the object" the moment the tool schema is serialized.
    private static readonly JsonElement Rationale =
        StringProp("Your brief reasoning for this decision (shown in the player's decision log).");

    /// <summary>The tool definitions passed to the Messages API on every request.</summary>
    public static IReadOnlyList<Tool> Definitions { get; } = Build();

    private static IReadOnlyList<Tool> Build() =>
    [
        Tool("get_character_state",
            "Return the current recorded character state as JSON. Call this at the start of a " +
            "session, or any time you're unsure of the current state before advising or deciding.",
            props: [], required: []),

        Tool("set_name",
            "Record the character's name.",
            props: [("name", StringProp("The character's name."))],
            required: ["name"]),

        Tool("set_class",
            "Set the character's class. You choose this — call it once you've decided what the player will play.",
            props: [("class", EnumProp("The chosen class.", Classes))],
            required: ["class"]),

        Tool("set_mode",
            "Set softcore or hardcore. You choose this.",
            props: [("mode", EnumProp("Softcore or Hardcore.", Modes))],
            required: ["mode"]),

        Tool("set_build_goal",
            "Record the build you're steering toward, e.g. \"Blizzard Sorceress\".",
            props: [("goal", StringProp("Short build name."))],
            required: ["goal"]),

        Tool("set_level",
            "Record the character's current level (1-99). Call this when the player levels up.",
            props: [("level", IntProp("Current level, 1-99."))],
            required: ["level"]),

        Tool("set_progress",
            "Record difficulty and act progress together.",
            props: [
                ("difficulty", EnumProp("Current difficulty.", Difficulties)),
                ("act", StringProp("Current act, e.g. \"Act 2\".")),
            ],
            required: ["difficulty", "act"]),

        Tool("set_attributes",
            "Record the absolute point totals invested across the four attributes.",
            props: [
                ("strength", IntProp("Points in Strength.")),
                ("dexterity", IntProp("Points in Dexterity.")),
                ("vitality", IntProp("Points in Vitality.")),
                ("energy", IntProp("Points in Energy.")),
            ],
            required: ["strength", "dexterity", "vitality", "energy"]),

        Tool("assign_skill_point",
            "Add points to a skill (default 1), creating it if new. Use when you tell the player to " +
            "spend a level-up point.",
            props: [
                ("skill", StringProp("Skill name, e.g. \"Frozen Orb\".")),
                ("tree", StringProp("Skill tree, e.g. \"Cold\" or \"Combat Masteries\".")),
                ("points", IntProp("How many points to add. Defaults to 1.")),
            ],
            required: ["skill", "tree"]),

        Tool("set_skill",
            "Set a skill to an absolute point total. Use to correct a value; 0 removes the skill.",
            props: [
                ("skill", StringProp("Skill name.")),
                ("tree", StringProp("Skill tree.")),
                ("points", IntProp("Absolute point total. 0 removes the skill.")),
            ],
            required: ["skill", "tree", "points"]),

        Tool("note_gear",
            "Record a piece of gear in a slot, replacing whatever was there.",
            props: [
                ("slot", StringProp("Slot, e.g. \"Weapon\", \"Helm\", \"Ring 1\".")),
                ("item", StringProp("Item or runeword name.")),
                ("notes", StringProp("Optional note on why it's equipped or what to seek next.")),
                ("quality", EnumProp("Item quality — colors the item like in-game text.", GearQualities)),
            ],
            required: ["slot", "item"]),

        Tool("add_reminder",
            "Add a short reminder for the player, e.g. \"Save the Larzuk socket quest\".",
            props: [("text", StringProp("The reminder text."))],
            required: ["text"]),

        Tool("log_run",
            "Record one completed farming run (a Mephisto run, a Baal run, Pindle, …). Call this " +
            "each time the player reports finishing a run — you keep their drop tally. The result " +
            "tells you the running count for that target.",
            props: [
                ("target", StringProp("What was farmed, e.g. \"Mephisto\".")),
                ("drop", StringProp("Notable drop(s), e.g. \"Shako!\". Omit when nothing worth naming.")),
            ],
            required: ["target"]),

        Tool("log_death",
            "Record the character's death and what caused it. Call this whenever the player " +
            "reports dying. In hardcore a death ends the character — mark the moment properly.",
            props: [("cause", StringProp("What killed them, e.g. \"Fire Enchanted boss pack in the Pit\"."))],
            required: ["cause"]),

        Tool("view_screenshot",
            "Look at the player's screen: fetches the screenshot they just took (the image on " +
            "their clipboard) so you can see it. Call this when the player says they've taken a " +
            "screenshot or asks you to look at something — read the scene, the stats, the item text.",
            props: [], required: []),

        Tool("lookup_reference",
            "Look up a D2 topic in the configured reference sites to orient or verify a decision. " +
            "Use sparingly — rely on your own knowledge first; only look up when a call should be " +
            "solid (exact breakpoints, current-patch specifics).",
            props: [("topic", StringProp("What to look up."))],
            required: ["topic"]),
    ];

    /// <summary>Runs a tool call against the service and returns the tool_result content string.</summary>
    public static string Execute(CharacterService service, string name, IReadOnlyDictionary<string, JsonElement> input)
    {
        var why = Str(input, "rationale");
        switch (name)
        {
            case "get_character_state":
                return JsonSerializer.Serialize(service.Current, StateJson);

            case "set_name":
                service.SetName(Str(input, "name") ?? "", DecisionSource.Claude, why);
                return "Recorded.";

            case "set_class":
                service.SetClass(ParseEnum<CharacterClass>(Str(input, "class")), DecisionSource.Claude, why);
                return "Recorded.";

            case "set_mode":
                service.SetMode(ParseEnum<GameMode>(Str(input, "mode")), DecisionSource.Claude, why);
                return "Recorded.";

            case "set_build_goal":
                service.SetBuildGoal(Str(input, "goal") ?? "", DecisionSource.Claude, why);
                return "Recorded.";

            case "set_level":
                service.SetLevel(Int(input, "level", 1), DecisionSource.Claude, why);
                return "Recorded.";

            case "set_progress":
                service.SetProgress(ParseEnum<Difficulty>(Str(input, "difficulty")), Str(input, "act") ?? "",
                    DecisionSource.Claude, why);
                return "Recorded.";

            case "set_attributes":
                service.SetAttributes(Int(input, "strength"), Int(input, "dexterity"),
                    Int(input, "vitality"), Int(input, "energy"), DecisionSource.Claude, why);
                return "Recorded.";

            case "assign_skill_point":
                service.AssignSkillPoint(Str(input, "skill") ?? "", Str(input, "tree") ?? "",
                    DecisionSource.Claude, Int(input, "points", 1), why);
                return "Recorded.";

            case "set_skill":
                service.SetSkill(Str(input, "skill") ?? "", Str(input, "tree") ?? "",
                    Int(input, "points", 0), DecisionSource.Claude, why);
                return "Recorded.";

            case "note_gear":
                service.NoteGear(Str(input, "slot") ?? "", Str(input, "item") ?? "",
                    DecisionSource.Claude, Str(input, "notes") ?? "", why,
                    ParseEnum<GearQuality>(Str(input, "quality")));
                return "Recorded.";

            case "add_reminder":
                service.AddReminder(Str(input, "text") ?? "", DecisionSource.Claude, why);
                return "Recorded.";

            case "log_run":
            {
                var target = Str(input, "target") ?? "";
                var count = service.LogRun(target, DecisionSource.Claude, Str(input, "drop") ?? "", why);
                return count == 0 ? "Nothing recorded — no target given." : $"Recorded — run #{count} for {target.Trim()}.";
            }

            case "log_death":
                service.LogDeath(Str(input, "cause") ?? "", DecisionSource.Claude, why);
                return "Recorded.";

            case "lookup_reference":
                // Reached only when no ReferenceLibrary is wired; the brain otherwise handles this async.
                return "No reference sites are configured — rely on your own D2 knowledge.";

            case "view_screenshot":
                // Reached only when no screenshot source is wired (e.g. the harness); the brain
                // otherwise answers with the clipboard image itself.
                return "No screenshot access in this session — ask the player to describe what they see.";

            default:
                return $"Unknown tool '{name}'.";
        }
    }

    // --- schema helpers --------------------------------------------------

    private static Tool Tool(string name, string description,
        (string Key, JsonElement Schema)[] props, string[] required)
    {
        var properties = new Dictionary<string, JsonElement>();
        foreach (var (key, schema) in props)
            properties[key] = schema;
        // Every mutation tool also accepts an optional rationale, logged with the change.
        // The read-only tools don't record anything, so they don't take one.
        if (name is not ("get_character_state" or "view_screenshot"))
            properties["rationale"] = Rationale;

        return new Tool
        {
            Name = name,
            Description = description,
            InputSchema = new()
            {
                Properties = properties,
                Required = required,
            },
        };
    }

    private static JsonElement StringProp(string description) =>
        JsonSerializer.SerializeToElement(new { type = "string", description });

    private static JsonElement IntProp(string description) =>
        JsonSerializer.SerializeToElement(new { type = "integer", description });

    private static JsonElement EnumProp(string description, string[] values) =>
        JsonSerializer.SerializeToElement(new { type = "string", description, @enum = values });

    // --- input parsing ---------------------------------------------------

    private static string? Str(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(IReadOnlyDictionary<string, JsonElement> input, string key, int fallback = 0)
    {
        if (input.TryGetValue(key, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var j)) return j;
        }
        return fallback;
    }

    private static TEnum ParseEnum<TEnum>(string? value) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : default;
}
