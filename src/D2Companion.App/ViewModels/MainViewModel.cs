using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2Companion.Brain;
using D2Companion.Core;
using D2Companion.Core.Domain;
using D2Companion.Core.Export;
using D2Companion.Core.Ledger;
using D2Companion.Voice;
using Microsoft.Win32;

namespace D2Companion.App.ViewModels;

/// <summary>
/// Backs the character sheet. Everything the player edits here is pushed through
/// <see cref="CharacterService"/> with <see cref="DecisionSource.Manual"/>, so manual
/// corrections land in the same ledger Claude will write to. When the service reports
/// a change (manual now; Claude's tool calls later), the whole view re-syncs from the
/// single source of truth.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly CharacterService _service;
    private readonly D2Brain? _brain;
    private readonly VoiceStack? _voice;
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly string _referencePath;

    // --- Voice / brain status ---
    [ObservableProperty] private string _voiceStatus = "";
    [ObservableProperty] private bool _isTalking;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScreenshotEnabled))]
    [NotifyCanExecuteChangedFor(nameof(NewConversationCommand))]
    private bool _isBusy;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ListenButtonText))] private bool _isListening;
    [ObservableProperty] private string _lastHeard = "";
    [ObservableProperty] private string _lastReply = "";

    // --- Character sheet fields (editable, applied via ApplySheet) ---
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private CharacterClass _selectedClass;
    [ObservableProperty] private GameMode _selectedMode;
    [ObservableProperty] private string _patchOrSeason = "";
    [ObservableProperty] private string _buildGoal = "";
    [ObservableProperty] private Difficulty _selectedDifficulty;
    [ObservableProperty] private string _act = "";
    [ObservableProperty] private int _level;
    [ObservableProperty] private int _strength;
    [ObservableProperty] private int _dexterity;
    [ObservableProperty] private int _vitality;
    [ObservableProperty] private int _energy;

    // --- Add/edit inputs ---
    [ObservableProperty] private string _newSkillName = "";
    [ObservableProperty] private string _newSkillTree = "";
    [ObservableProperty] private int _newSkillPoints = 1;
    [ObservableProperty] private SkillAllocation? _selectedSkill;

    [ObservableProperty] private string _newGearSlot = "";
    [ObservableProperty] private string _newGearItem = "";
    [ObservableProperty] private string _newGearNotes = "";
    [ObservableProperty] private GearQuality _newGearQuality = GearQuality.Unknown;
    [ObservableProperty] private GearItem? _selectedGear;

    [ObservableProperty] private string _newReminder = "";
    [ObservableProperty] private string? _selectedReminder;

    public ObservableCollection<SkillAllocation> Skills { get; } = new();
    public ObservableCollection<GearItem> Gear { get; } = new();
    public ObservableCollection<string> Reminders { get; } = new();
    public ObservableCollection<RunSummaryRow> RunSummaries { get; } = new();
    public ObservableCollection<DeathRow> Deaths { get; } = new();
    public ObservableCollection<LedgerRow> Ledger { get; } = new();

    public IReadOnlyList<CharacterClass> Classes { get; } = Enum.GetValues<CharacterClass>();
    public IReadOnlyList<GameMode> Modes { get; } = Enum.GetValues<GameMode>();
    public IReadOnlyList<Difficulty> Difficulties { get; } = Enum.GetValues<Difficulty>();
    public IReadOnlyList<GearQuality> Qualities { get; } = Enum.GetValues<GearQuality>();

    public MainViewModel(CharacterService service, D2Brain? brain, VoiceStack? voice, AppConfig config)
    {
        _service = service;
        _brain = brain;
        _voice = voice;
        _settings = config.Settings;
        _settingsPath = config.SettingsPath;
        _referencePath = config.ReferencePath;
        _service.Changed += OnServiceChanged;
        SyncFromModel();
        RefreshLedger();

        if (_voice is not null)
            _voice.OpenMic.UtteranceCaptured += OnUtteranceCaptured;

        VoiceStatus = InitialVoiceStatus();
    }

    /// <summary>Voice is available only when both the brain and the voice stack are configured.</summary>
    public bool VoiceEnabled => _brain is not null && _voice is not null;

    /// <summary>Screenshots only need the brain — they work even without ElevenLabs keys.</summary>
    public bool ScreenshotEnabled => _brain is not null && !IsBusy;

    public bool IsPushToTalk => _settings.VoiceMode == VoiceMode.PushToTalk;
    public bool IsOpenMic => _settings.VoiceMode == VoiceMode.OpenMic;
    public string ListenButtonText => IsListening ? "⏹  Stop listening" : "🎤  Start listening";

    private string InitialVoiceStatus()
    {
        if (!VoiceEnabled)
            return "Add your Anthropic + ElevenLabs keys (user-secrets) to enable voice.";
        return IsOpenMic ? "Open-mic mode — press Start listening." : "Hold the talk button and speak.";
    }

    private void OnServiceChanged(object? sender, CharacterChangedEventArgs e)
    {
        // Future-proof for the Phase 1 brain, which may fire from a background thread.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.Invoke(() => { SyncFromModel(); RefreshLedger(); });
        else
        {
            SyncFromModel();
            RefreshLedger();
        }
    }

    /// <summary>Pushes the scalar sheet fields into the service. Unchanged values are ignored
    /// by the service, so only real edits get logged.</summary>
    [RelayCommand]
    private void ApplySheet()
    {
        _service.SetName(Name, DecisionSource.Manual);
        _service.SetClass(SelectedClass, DecisionSource.Manual);
        _service.SetMode(SelectedMode, DecisionSource.Manual);
        _service.SetPatchOrSeason(PatchOrSeason, DecisionSource.Manual);
        _service.SetBuildGoal(BuildGoal, DecisionSource.Manual);
        _service.SetDifficulty(SelectedDifficulty, DecisionSource.Manual);
        _service.SetAct(Act, DecisionSource.Manual);
        _service.SetLevel(Level, DecisionSource.Manual);
        _service.SetAttributes(Strength, Dexterity, Vitality, Energy, DecisionSource.Manual);
    }

    // --- Data management ---

    /// <summary>Backs up the current run, then clears the character and ledger for a fresh start.</summary>
    [RelayCommand]
    private void NewCharacter()
    {
        var confirm = MessageBox.Show(
            "Start a new character? The current character and its decision log will be cleared. " +
            "A backup is saved automatically first.",
            "New character", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        if (!TryBackupOrConfirm()) return;

        _service.NewCharacter(DecisionSource.Manual);
        _brain?.Reset();
        LastHeard = "";
        LastReply = "";
        VoiceStatus = "New character started.";
    }

    /// <summary>Clears Claude's conversation memory mid-session — a cost and latency reset.
    /// The character, ledger, runs, and deaths are untouched; Claude re-orients from the
    /// recorded state on the next turn.</summary>
    [RelayCommand(CanExecute = nameof(CanNewConversation))]
    private void NewConversation()
    {
        _brain!.Reset();
        LastHeard = "";
        LastReply = "";
        VoiceStatus = "Conversation cleared — your character is untouched. Claude re-reads the sheet on the next turn.";
    }

    private bool CanNewConversation() => _brain is not null && !IsBusy;

    /// <summary>Loads a previously exported character (JSON), replacing the current one.</summary>
    [RelayCommand]
    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import a character export (JSON)",
            Filter = "JSON data (*.json)|*.json",
            DefaultExt = ".json",
        };
        if (dialog.ShowDialog() != true) return;

        CharacterExport? export;
        try
        {
            export = CharacterImporter.FromJson(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't read the file: {ex.Message}", "Import failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (export?.Character is null)
        {
            MessageBox.Show("That file isn't a valid D2Companion export.", "Import failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var confirm = MessageBox.Show(
            "Import this character? It replaces the current character and log. A backup is saved first.",
            "Import", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        if (!TryBackupOrConfirm()) return;

        _service.ImportCharacter(export, DecisionSource.Manual);
        _brain?.Reset();
        LastHeard = "";
        LastReply = "";
        VoiceStatus = "Character imported.";
    }

    /// <summary>Writes an automatic backup; if it fails, asks whether to proceed. Returns
    /// false only when the user declines after a failed backup — so no silent data loss.</summary>
    private bool TryBackupOrConfirm()
    {
        try
        {
            WriteSnapshot();
            return true;
        }
        catch (Exception ex)
        {
            return MessageBox.Show(
                $"Couldn't save the automatic backup ({ex.Message}). Continue anyway?",
                "Backup failed", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }
    }

    /// <summary>Exports the character + full decision log as JSON or a Markdown build log.</summary>
    [RelayCommand]
    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export character + decision log",
            FileName = $"d2companion-{DateTime.Now:yyyyMMdd-HHmm}",
            Filter = "JSON data (*.json)|*.json|Markdown build log (*.md)|*.md",
            DefaultExt = ".json",
        };
        if (dialog.ShowDialog() != true) return;

        var ledger = _service.FullLedger();
        var content = dialog.FilterIndex == 2
            ? CharacterExporter.ToMarkdown(_service.Current, ledger)
            : CharacterExporter.ToJson(_service.Current, ledger);
        File.WriteAllText(dialog.FileName, content);
        VoiceStatus = $"Exported to {dialog.FileName}";
    }

    private string WriteSnapshot()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D2Companion", "backups");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, CharacterExporter.ToJson(_service.Current, _service.FullLedger()));
        return path;
    }

    [RelayCommand]
    private void AddOrUpdateSkill() =>
        _service.SetSkill(NewSkillName, NewSkillTree, NewSkillPoints, DecisionSource.Manual);

    [RelayCommand]
    private void RemoveSelectedSkill()
    {
        if (SelectedSkill is { } skill)
            _service.SetSkill(skill.Skill, skill.Tree, 0, DecisionSource.Manual);
    }

    [RelayCommand]
    private void AddOrUpdateGear() =>
        _service.NoteGear(NewGearSlot, NewGearItem, DecisionSource.Manual, NewGearNotes, quality: NewGearQuality);

    [RelayCommand]
    private void RemoveSelectedGear()
    {
        if (SelectedGear is { } gear)
            _service.RemoveGear(gear.Slot, DecisionSource.Manual);
    }

    [RelayCommand]
    private void AddReminder() => _service.AddReminder(NewReminder, DecisionSource.Manual);

    [RelayCommand]
    private void RemoveSelectedReminder()
    {
        if (SelectedReminder is { } reminder)
            _service.RemoveReminder(reminder, DecisionSource.Manual);
    }

    // --- Voice: one push-to-talk turn ---

    /// <summary>Talk-button pressed: start capturing the mic.</summary>
    public void BeginTalk()
    {
        if (!VoiceEnabled || IsTalking || IsBusy) return;
        try
        {
            _voice!.Recorder.Start();
        }
        catch (Exception ex)
        {
            // Unplugged headset / device claimed elsewhere — report, don't crash mid-game.
            VoiceStatus = $"Couldn't open the microphone: {ex.Message}";
            return;
        }
        IsTalking = true;
        VoiceStatus = "Listening…";
    }

    /// <summary>Talk-button released: transcribe, ask Claude, speak the reply. Claude's tool
    /// calls update the character mid-turn, and the sheet re-syncs via the Changed event.
    /// Must never throw — it's awaited from an async void mouse handler.</summary>
    public async Task EndTalkAsync()
    {
        if (!VoiceEnabled || !IsTalking) return;
        IsTalking = false;
        byte[] wav;
        try
        {
            wav = await Task.Run(() => _voice!.Recorder.Stop());
        }
        catch (Exception ex)
        {
            VoiceStatus = $"Microphone capture failed: {ex.Message}";
            return;
        }
        await RespondToAsync(wav);
    }

    // --- Open-mic (hands-free) ---

    /// <summary>Starts/stops continuous listening in open-mic mode.</summary>
    [RelayCommand]
    private void ToggleListening()
    {
        if (!VoiceEnabled) return;
        if (IsListening) StopListening();
        else StartListening();
    }

    private void StartListening()
    {
        try
        {
            _voice!.OpenMic.Start();
        }
        catch (Exception ex)
        {
            VoiceStatus = $"Couldn't open the microphone: {ex.Message}";
            return;
        }
        IsListening = true;
        VoiceStatus = "Listening…";
    }

    private void StopListening()
    {
        _voice!.OpenMic.Stop();
        IsListening = false;
        VoiceStatus = "Stopped listening.";
    }

    private void OnUtteranceCaptured(byte[] wav)
    {
        // Fires on the audio thread; hop to the UI thread to run the turn, then resume hearing
        // (the listener auto-suspended so it doesn't transcribe Claude's own reply).
        Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            await RespondToAsync(wav);
            if (IsListening)
                _voice!.OpenMic.Resume();
        });
    }

    /// <summary>Shared voice turn: transcribe → clean → brain → speak. Used by both modes.</summary>
    private async Task RespondToAsync(byte[] wav)
    {
        IsBusy = true;
        try
        {
            VoiceStatus = "Transcribing…";
            var transcript = await _voice!.Stt.TranscribeAsync(wav);
            var cleaned = TranscriptCleaner.Clean(transcript);
            if (cleaned is null)
            {
                VoiceStatus = IsListening ? "Listening…" : "Didn't catch that — try again.";
                return;
            }

            LastHeard = cleaned;
            VoiceStatus = "Thinking…";
            var reply = await _brain!.SendAsync(cleaned);

            LastReply = reply;
            await SpeakAsync(reply);
        }
        catch (Exception ex)
        {
            VoiceStatus = $"Voice error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Speaks a reply when voice is configured; otherwise just settles the status.</summary>
    private async Task SpeakAsync(string reply)
    {
        if (_voice is null)
        {
            VoiceStatus = "Ready.";
            return;
        }
        VoiceStatus = "Speaking…";
        var audio = await _voice.Tts.SynthesizeAsync(reply);
        await _voice.Player.PlayMp3Async(audio);
        VoiceStatus = IsListening ? "Listening…"
            : IsPushToTalk ? "Ready — hold to talk again."
            : "Ready.";
    }

    // --- Screenshot as eyes ---

    /// <summary>Shares a screenshot with Claude: the clipboard image when there is one
    /// (PrtScn → Ctrl+V flow), otherwise a file picker.</summary>
    [RelayCommand]
    private async Task ShareScreenshotAsync()
    {
        if (!TryReadClipboardImage(out var clipboardImage)) return;
        if (clipboardImage is not null)
        {
            await SendScreenshotAsync(clipboardImage);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Share a screenshot with Claude",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
        };
        if (dialog.ShowDialog() != true) return;
        await SendScreenshotAsync(new BitmapImage(new Uri(dialog.FileName)));
    }

    /// <summary>Ctrl+V anywhere outside a text box: paste the clipboard screenshot.</summary>
    [RelayCommand]
    private async Task PasteScreenshotAsync()
    {
        if (!TryReadClipboardImage(out var image)) return;
        if (image is null)
        {
            VoiceStatus = "Clipboard has no image — hit PrtScn in game first.";
            return;
        }
        await SendScreenshotAsync(image);
    }

    /// <summary>Reads the clipboard image if any. The Windows clipboard throws when another
    /// app briefly holds it locked — returns false (with a status hint) instead of crashing.</summary>
    private bool TryReadClipboardImage(out BitmapSource? image)
    {
        try
        {
            image = Clipboard.ContainsImage() ? Clipboard.GetImage() : null;
            return true;
        }
        catch (Exception ex)
        {
            image = null;
            VoiceStatus = $"Couldn't read the clipboard ({ex.Message}) — try again in a second.";
            return false;
        }
    }

    private async Task SendScreenshotAsync(BitmapSource? image)
    {
        if (image is null || _brain is null || IsBusy) return;
        IsBusy = true;
        try
        {
            VoiceStatus = "Reading your screen…";
            LastHeard = "(screenshot)";
            var jpeg = ScreenshotEncoder.ToJpeg(image);
            var reply = await _brain.SendAsync(
                "Here's a screenshot of my game — you're seeing through my eyes. " +
                "Look it over: react, decide, and record anything worth recording.",
                jpeg, "image/jpeg");

            LastReply = reply;
            await SpeakAsync(reply);
        }
        catch (Exception ex)
        {
            VoiceStatus = $"Screenshot error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- Settings ---

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new SettingsWindow(_settings, _settingsPath, _referencePath)
        {
            Owner = Application.Current?.MainWindow,
        };
        if (window.ShowDialog() == true)
            ApplyVoiceMode();
    }

    /// <summary>Applies the voice-mode change live; threshold/model changes need a restart.</summary>
    private void ApplyVoiceMode()
    {
        if (!IsOpenMic && IsListening)
            StopListening();
        OnPropertyChanged(nameof(IsPushToTalk));
        OnPropertyChanged(nameof(IsOpenMic));
        VoiceStatus = InitialVoiceStatus() + "  (thresholds & voice apply after restart)";
    }

    private void SyncFromModel()
    {
        var c = _service.Current;
        Name = c.Name;
        SelectedClass = c.Class;
        SelectedMode = c.Mode;
        PatchOrSeason = c.PatchOrSeason;
        BuildGoal = c.BuildGoal;
        SelectedDifficulty = c.Difficulty;
        Act = c.Act;
        Level = c.Level;
        Strength = c.Attributes.Strength;
        Dexterity = c.Attributes.Dexterity;
        Vitality = c.Attributes.Vitality;
        Energy = c.Attributes.Energy;

        Replace(Skills, c.Skills);
        Replace(Gear, c.Gear);
        Replace(Reminders, c.Reminders);
        Replace(RunSummaries, c.Runs
            .GroupBy(r => r.Target, StringComparer.OrdinalIgnoreCase)
            .Select(g => new RunSummaryRow(g.Key, g.ToList())));
        Replace(Deaths, c.Deaths.Select(d => new DeathRow(d)));
    }

    private void RefreshLedger()
    {
        Ledger.Clear();
        foreach (var entry in _service.RecentLedger())
            Ledger.Add(new LedgerRow(entry));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }
}
