using D2Companion.Brain;
using D2Companion.Brain.Reference;
using D2Companion.Core;
using D2Companion.Core.Domain;
using D2Companion.Core.Export;
using D2Companion.Core.Ledger;
using D2Companion.Core.Persistence;
using Microsoft.Extensions.Configuration;

// Headless harness. It reads the Anthropic key from the SAME place the WPF app does —
// the shared user-secrets store (UserSecretsId "d2companion-app-secrets") — falling back
// to secrets.local.json and then the ANTHROPIC_API_KEY environment variable. With a key
// it opens a live text chat with the Claude brain and shows each decision landing in the
// ledger; without one it runs the offline simulation. Either way, no GUI required.

var config = new ConfigurationBuilder()
    .AddUserSecrets("d2companion-app-secrets") // same id as D2Companion.App
    .AddJsonFile("secrets.local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var apiKey = config["Anthropic:ApiKey"] ?? config["ANTHROPIC_API_KEY"];

if (string.IsNullOrWhiteSpace(apiKey))
{
    RunSimulation();
    Console.WriteLine("\nTip: set ANTHROPIC_API_KEY and re-run to chat with the live brain.");
    return;
}

await RunChatAsync(apiKey);

// ---------------------------------------------------------------------------

static async Task RunChatAsync(string apiKey)
{
    var directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D2Companion");
    Directory.CreateDirectory(directory);

    // A harness-specific DB so chatting here doesn't clobber the WPF app's character.
    var store = new SqliteCharacterStore(Path.Combine(directory, "harness.db"));
    var service = new CharacterService(store);
    var reference = new ReferenceLibrary(
        ReferenceConfig.LoadOrCreate(Path.Combine(directory, "reference-sites.json")));
    var brain = new D2Brain(service, new BrainOptions { ApiKey = apiKey }, reference);

    // Live view of decisions as Claude's tool calls record them.
    service.Changed += (_, e) =>
    {
        var who = e.Entry.Source == DecisionSource.Claude ? "Claude" : "you";
        Console.WriteLine($"   · [{who}] {e.Entry.Details}");
    };

    Console.WriteLine("=== D2Companion brain (text chat) ===");
    Console.WriteLine("Type to talk. Commands: 'state' dumps the sheet, 'new' starts a fresh");
    Console.WriteLine("character, 'export' writes JSON+Markdown, 'import <path>' loads a JSON export,");
    Console.WriteLine("'see <image-path> [message]' shows Claude a screenshot, 'reset' clears the");
    Console.WriteLine("conversation, 'quit' exits. Character persists between runs.\n");

    while (true)
    {
        Console.Write("you> ");
        var line = Console.ReadLine();
        if (line is null) break;
        line = line.Trim();
        if (line.Length == 0) continue;
        if (line is "quit" or "exit") break;
        if (line is "reset") { brain.Reset(); Console.WriteLine("(conversation cleared)\n"); continue; }
        if (line is "state") { PrintState(service.Current); Console.WriteLine(); continue; }
        if (line is "new")
        {
            service.NewCharacter(DecisionSource.Manual);
            brain.Reset();
            Console.WriteLine("(new character — data cleared)\n");
            continue;
        }
        if (line is "export") { ExportToFiles(service); continue; }
        if (line.StartsWith("import ", StringComparison.OrdinalIgnoreCase))
        {
            var path = line["import ".Length..].Trim().Trim('"');
            try
            {
                var export = CharacterImporter.FromJson(File.ReadAllText(path));
                if (export?.Character is null)
                {
                    Console.WriteLine("Not a valid D2Companion export.\n");
                }
                else
                {
                    service.ImportCharacter(export, DecisionSource.Manual);
                    brain.Reset();
                    Console.WriteLine("(character imported)\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[import error] {ex.Message}\n");
            }
            continue;
        }

        if (line.StartsWith("see ", StringComparison.OrdinalIgnoreCase))
        {
            var (path, message) = SplitPathAndMessage(line["see ".Length..]);
            try
            {
                var bytes = File.ReadAllBytes(path);
                var text = message.Length > 0
                    ? message
                    : "Here's a screenshot of my game — you're seeing through my eyes. " +
                      "Look it over: react, decide, and record anything worth recording.";
                var reply = await brain.SendAsync(text, bytes, MediaTypeFor(path));
                Console.WriteLine($"claude> {reply}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[see error] {ex.Message}\n");
            }
            continue;
        }

        try
        {
            var reply = await brain.SendAsync(line);
            Console.WriteLine($"claude> {reply}\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[error] {ex.Message}\n");
        }
    }
}

// Splits `see` input into a path (quoted or up to the first space) and an optional message.
static (string Path, string Message) SplitPathAndMessage(string input)
{
    input = input.Trim();
    if (input.StartsWith('"'))
    {
        var close = input.IndexOf('"', 1);
        if (close > 0)
            return (input[1..close], input[(close + 1)..].Trim());
    }
    var space = input.IndexOf(' ');
    return space < 0 ? (input, "") : (input[..space], input[(space + 1)..].Trim());
}

static string MediaTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
{
    ".jpg" or ".jpeg" => "image/jpeg",
    ".gif" => "image/gif",
    ".webp" => "image/webp",
    _ => "image/png",
};

static void RunSimulation()
{
    var dbPath = Path.Combine(AppContext.BaseDirectory, "harness.db");
    if (File.Exists(dbPath))
        File.Delete(dbPath); // start each simulation from a clean slate

    var store = new SqliteCharacterStore(dbPath);
    var service = new CharacterService(store);

    Console.WriteLine("=== D2Companion harness: offline simulation of Claude-driven decisions ===\n");

    const DecisionSource claude = DecisionSource.Claude;

    service.SetName("Wraithsong", claude, "Player asked to start a new character.");
    service.SetClass(CharacterClass.Sorceress, claude, "Blizzard sorc: strong, beginner-friendly, great magic-find later.");
    service.SetMode(GameMode.Softcore, claude, "New to the ladder — softcore to learn the fights.");
    service.SetPatchOrSeason("2.8 Ladder", claude);
    service.SetBuildGoal("Blizzard Sorceress", claude, "Cold tree, Blizzard as the main nuke.");
    service.SetProgress(Difficulty.Normal, "Act 1", claude);
    service.SetLevel(6, claude, "Dinged 6 clearing the Blood Moor and Den.");
    service.AssignSkillPoint("Ice Bolt", "Cold", claude, rationale: "Synergy + prereq for Blizzard.");
    service.AssignSkillPoint("Frost Nova", "Cold", claude, rationale: "Prereq for Blizzard.");
    service.SetAttributes(0, 0, 15, 0, claude, "Everything into Vitality for now.");
    service.NoteGear("Weapon", "Leaf (Tir + Ral)", claude, notes: "+3 Fire skills base — cheap early staff.");
    service.AddReminder("Save the Act 1 Larzuk socket quest for a better base later.", claude);
    service.SetLevel(7, DecisionSource.Manual, "Player corrected: actually hit 7.");

    PrintState(service.Current);
    PrintLedger(store.LoadLedger());
    Console.WriteLine($"\nDatabase written to: {dbPath}");
}

static void PrintState(Character c)
{
    Console.WriteLine("--- Character ---");
    Console.WriteLine($"  {Blank(c.Name)}  |  {c.Class}  |  {c.Mode}  |  {Blank(c.PatchOrSeason)}");
    Console.WriteLine($"  Goal: {Blank(c.BuildGoal)}");
    Console.WriteLine($"  {c.Difficulty} · {Blank(c.Act)} · Level {c.Level}");
    Console.WriteLine($"  Attributes: STR {c.Attributes.Strength} / DEX {c.Attributes.Dexterity} / VIT {c.Attributes.Vitality} / ENE {c.Attributes.Energy}");

    if (c.Skills.Count > 0)
    {
        Console.WriteLine("  Skills:");
        foreach (var s in c.Skills)
            Console.WriteLine($"    - {s.Skill} ({s.Tree}): {s.Points}");
    }

    if (c.Gear.Count > 0)
    {
        Console.WriteLine("  Gear:");
        foreach (var g in c.Gear)
            Console.WriteLine($"    - {g.Slot}: {g.Item}{(string.IsNullOrEmpty(g.Notes) ? "" : $"  [{g.Notes}]")}");
    }

    if (c.Reminders.Count > 0)
    {
        Console.WriteLine("  Reminders:");
        foreach (var r in c.Reminders)
            Console.WriteLine($"    - {r}");
    }
}

static void PrintLedger(IReadOnlyList<LedgerEntry> entries)
{
    Console.WriteLine($"\n--- Decision ledger ({entries.Count} entries, newest first) ---");
    foreach (var e in entries)
    {
        var who = e.Source == DecisionSource.Claude ? "Claude" : "Manual";
        Console.WriteLine($"  [{e.TimestampUtc:HH:mm:ss}] ({who}) {e.Details}");
        if (e.Rationale is not null)
            Console.WriteLine($"           ↳ {e.Rationale}");
    }
}

static string Blank(string value) => string.IsNullOrEmpty(value) ? "—" : value;

static void ExportToFiles(CharacterService service)
{
    var directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D2Companion", "exports");
    Directory.CreateDirectory(directory);

    var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    var ledger = service.FullLedger();
    var jsonPath = Path.Combine(directory, $"export-{stamp}.json");
    var markdownPath = Path.Combine(directory, $"export-{stamp}.md");

    File.WriteAllText(jsonPath, CharacterExporter.ToJson(service.Current, ledger));
    File.WriteAllText(markdownPath, CharacterExporter.ToMarkdown(service.Current, ledger));

    Console.WriteLine($"Exported:\n  {jsonPath}\n  {markdownPath}\n");
}
