using System;
using System.IO;
using System.Windows;
using D2Companion.App.ViewModels;
using D2Companion.Brain;
using D2Companion.Brain.Reference;
using D2Companion.Core;
using D2Companion.Core.Persistence;
using D2Companion.Voice;
using Microsoft.Extensions.Configuration;

namespace D2Companion.App;

/// <summary>
/// Composition root. Builds the store + service, then optionally the Claude brain and the
/// ElevenLabs voice stack — but only if their keys are configured. With no keys the app
/// still runs as the Phase 0 manual character sheet; add keys and voice lights up.
///
/// Keys resolve from (in order): dotnet user-secrets, a gitignored secrets.local.json next
/// to the executable, then environment variables.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var config = new ConfigurationBuilder()
            .AddUserSecrets<App>(optional: true)
            .AddJsonFile("secrets.local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D2Companion");
        Directory.CreateDirectory(directory);

        var settingsPath = Path.Combine(directory, "settings.json");
        var referencePath = Path.Combine(directory, "reference-sites.json");
        var settings = SettingsStore.Load(settingsPath);

        var store = new SqliteCharacterStore(Path.Combine(directory, "d2companion.db"));
        var service = new CharacterService(store);

        var brain = TryCreateBrain(config, service, referencePath, settings);
        var voice = TryCreateVoice(config, settings);

        var viewModel = new MainViewModel(service, brain, voice, new AppConfig(settings, settingsPath, referencePath));
        var window = new MainWindow { DataContext = viewModel };
        RestoreWindowBounds(window, settings);
        window.Closing += (_, _) => PersistWindowBounds(window, settings, settingsPath);
        window.Show();
    }

    private static void RestoreWindowBounds(Window window, AppSettings settings)
    {
        if (settings.WindowLeft is { } left && settings.WindowTop is { } top &&
            settings.WindowWidth is { } width && width > 0 &&
            settings.WindowHeight is { } height && height > 0)
        {
            // Only restore if the saved rect is at least partly on a currently-connected
            // monitor — otherwise a since-disconnected second screen would hide the window.
            var virtualScreen = new Rect(
                SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (virtualScreen.IntersectsWith(new Rect(left, top, width, height)))
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = left;
                window.Top = top;
                window.Width = width;
                window.Height = height;
            }
        }

        if (settings.WindowMaximized)
            window.WindowState = WindowState.Maximized;
    }

    private static void PersistWindowBounds(Window window, AppSettings settings, string settingsPath)
    {
        settings.WindowMaximized = window.WindowState == WindowState.Maximized;

        // RestoreBounds is the normal-state rect even when currently maximized.
        var bounds = window.RestoreBounds;
        if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
        {
            settings.WindowLeft = bounds.Left;
            settings.WindowTop = bounds.Top;
            settings.WindowWidth = bounds.Width;
            settings.WindowHeight = bounds.Height;
        }

        try
        {
            SettingsStore.Save(settingsPath, settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort on shutdown — don't block closing if the save fails.
        }
    }

    private static D2Brain? TryCreateBrain(IConfiguration config, CharacterService service, string referencePath, AppSettings settings)
    {
        var apiKey = config["Anthropic:ApiKey"] ?? config["ANTHROPIC_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        // Editable seed-site list (created with defaults on first run).
        var reference = new ReferenceLibrary(ReferenceConfig.LoadOrCreate(referencePath));
        return new D2Brain(service, new BrainOptions { ApiKey = apiKey, Effort = settings.Effort }, reference,
            CaptureClipboardImage);
    }

    /// <summary>Claude's on-demand eyes (the view_screenshot tool): the clipboard image,
    /// JPEG-encoded for the vision API. Marshals to the dispatcher because the clipboard
    /// is UI-thread-only and the tool loop may resume elsewhere.</summary>
    private static CapturedImage? CaptureClipboardImage() =>
        Current.Dispatcher.Invoke(() =>
            Clipboard.ContainsImage() && Clipboard.GetImage() is { } image
                ? new CapturedImage(ScreenshotEncoder.ToJpeg(image), "image/jpeg")
                : null);

    private static VoiceStack? TryCreateVoice(IConfiguration config, AppSettings settings)
    {
        var apiKey = config["ElevenLabs:ApiKey"];
        var voiceId = config["ElevenLabs:VoiceId"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(voiceId))
            return null;

        var options = new ElevenLabsOptions { ApiKey = apiKey, VoiceId = voiceId, TtsModel = settings.TtsModel };
        var openMic = new OpenMicListener(settings.SilenceThreshold, settings.EndpointSilenceMs, settings.MinSpeechMs);
        return new VoiceStack(
            new PushToTalkRecorder(),
            new ElevenLabsSpeechToText(options),
            new ElevenLabsTextToSpeech(options),
            new AudioPlayer(),
            openMic);
    }
}
