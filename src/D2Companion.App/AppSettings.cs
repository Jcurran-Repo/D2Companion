using D2Companion.Voice;

namespace D2Companion.App;

/// <summary>User-tunable settings, persisted to settings.json in %LocalAppData%\D2Companion.</summary>
public sealed class AppSettings
{
    public VoiceMode VoiceMode { get; set; } = VoiceMode.PushToTalk;

    // Open-mic voice-activity detection.
    public double SilenceThreshold { get; set; } = 700;
    public int EndpointSilenceMs { get; set; } = 900;
    public int MinSpeechMs { get; set; } = 350;

    // Brain / voice.
    public string Model { get; set; } = "claude-opus-4-8";
    public string TtsModel { get; set; } = "eleven_turbo_v2_5";
    public string Effort { get; set; } = "low";

    // Main window placement — remembered across runs (nice on a multi-monitor setup).
    // Null on first run, so the window opens at its default centered position.
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
}

/// <summary>Everything the main view-model needs about configuration and where it lives.</summary>
public sealed record AppConfig(AppSettings Settings, string SettingsPath, string ReferencePath);
