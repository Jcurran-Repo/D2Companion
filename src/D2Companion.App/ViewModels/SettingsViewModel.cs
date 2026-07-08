using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2Companion.Brain.Reference;
using D2Companion.Voice;

namespace D2Companion.App.ViewModels;

/// <summary>An editable seed-site row for the settings grid.</summary>
public sealed class SiteRow
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly string _referencePath;

    [ObservableProperty] private VoiceMode _selectedMode;
    [ObservableProperty] private double _silenceThreshold;
    [ObservableProperty] private int _endpointSilenceMs;
    [ObservableProperty] private int _minSpeechMs;
    [ObservableProperty] private string _effort = "low";
    [ObservableProperty] private string _ttsModel = "";
    [ObservableProperty] private int _maxReferenceChars;

    public ObservableCollection<SiteRow> Sites { get; } = new();
    public IReadOnlyList<VoiceMode> Modes { get; } = Enum.GetValues<VoiceMode>();
    public IReadOnlyList<string> Efforts { get; } = new[] { "low", "medium", "high" };

    // --- Open-mic tuning (live meter; no STT/API calls, no restart needed) ---

    private readonly MicTuner _tuner = new();
    private readonly object _measureGate = new();
    private List<double>? _measureSamples;
    private double _noisePeak;
    private double _speechTypical;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(MeterButtonText))] private bool _isTuning;
    [ObservableProperty] private bool _isMeasuring;
    [ObservableProperty] private double _currentEnergy;
    [ObservableProperty] private double _peakEnergy;
    [ObservableProperty] private string _tunerState = "quiet";
    [ObservableProperty] private string _noiseResult = "";
    [ObservableProperty] private string _voiceResult = "";
    [ObservableProperty] private string _suggestionText = "";
    [ObservableProperty] private double _suggestedThreshold;

    public ObservableCollection<string> TuningLog { get; } = new();
    public string MeterButtonText => IsTuning ? "⏹  Stop meter" : "🎙  Start meter";

    public SettingsViewModel(AppSettings settings, string settingsPath, string referencePath)
    {
        _settings = settings;
        _settingsPath = settingsPath;
        _referencePath = referencePath;

        SelectedMode = settings.VoiceMode;
        SilenceThreshold = settings.SilenceThreshold;
        EndpointSilenceMs = settings.EndpointSilenceMs;
        MinSpeechMs = settings.MinSpeechMs;
        Effort = settings.Effort;
        TtsModel = settings.TtsModel;

        var reference = ReferenceConfig.LoadOrCreate(referencePath);
        MaxReferenceChars = reference.MaxCharsReturned;
        foreach (var source in reference.Sources)
            Sites.Add(new SiteRow { Name = source.Name, Url = source.Url });
    }

    // --- Tuning commands -------------------------------------------------

    [RelayCommand]
    private void ToggleMeter()
    {
        if (IsTuning) StopTuning();
        else StartTuning();
    }

    private void StartTuning()
    {
        _tuner.FrameMeasured += OnFrameMeasured;
        _tuner.UtteranceEnded += OnUtteranceEnded;
        try
        {
            _tuner.Start(SilenceThreshold, EndpointSilenceMs, MinSpeechMs);
        }
        catch (Exception ex)
        {
            // No mic / device busy — report it in the log instead of crashing Settings.
            _tuner.FrameMeasured -= OnFrameMeasured;
            _tuner.UtteranceEnded -= OnUtteranceEnded;
            Log($"Couldn't open the microphone: {ex.Message}");
            return;
        }
        PeakEnergy = 0;
        IsTuning = true;
        Log("Meter started — the bar shows what the mic hears right now.");
    }

    /// <summary>Stops the meter and releases the mic. Safe to call when not running.</summary>
    public void StopTuning()
    {
        if (!IsTuning) return;
        _tuner.Stop();
        _tuner.FrameMeasured -= OnFrameMeasured;
        _tuner.UtteranceEnded -= OnUtteranceEnded;
        IsTuning = false;
        TunerState = "quiet";
    }

    /// <summary>Phase 1: 10 seconds of game audio only — captures the noise peak.</summary>
    [RelayCommand]
    private async Task MeasureNoiseAsync()
    {
        var samples = await CollectSamplesAsync();
        if (samples is null) return;
        _noisePeak = TuningMath.Percentile(samples, 0.99); // peak, ignoring one-off spikes
        NoiseResult = $"Game noise peak: {_noisePeak:F0}";
        Log($"Noise measured — peak {_noisePeak:F0}.");
        UpdateSuggestion();
    }

    /// <summary>Phase 2: 10 seconds of talking over the game — captures typical speech energy.</summary>
    [RelayCommand]
    private async Task MeasureVoiceAsync()
    {
        var samples = await CollectSamplesAsync();
        if (samples is null) return;
        _speechTypical = TuningMath.Percentile(samples, 0.75); // where your voice mostly sits
        VoiceResult = $"Your voice (typical): {_speechTypical:F0}";
        Log($"Voice measured — typical {_speechTypical:F0}.");
        UpdateSuggestion();
    }

    [RelayCommand]
    private void ApplySuggestion()
    {
        if (SuggestedThreshold <= 0) return;
        SilenceThreshold = SuggestedThreshold;
        Log($"Silence threshold set to {SuggestedThreshold:F0} — Save, then restart the app to apply to real listening.");
    }

    private async Task<List<double>?> CollectSamplesAsync()
    {
        if (IsMeasuring) return null;
        if (!IsTuning) StartTuning();

        IsMeasuring = true;
        var samples = new List<double>(capacity: 256);
        lock (_measureGate) _measureSamples = samples;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
        finally
        {
            lock (_measureGate) _measureSamples = null;
            IsMeasuring = false;
        }
        return samples;
    }

    private void UpdateSuggestion()
    {
        if (_noisePeak <= 0 || _speechTypical <= 0)
        {
            SuggestionText = "Measure both game noise and your voice to get a suggestion.";
            return;
        }

        var suggestion = TuningMath.SuggestThreshold(_noisePeak, _speechTypical);
        SuggestedThreshold = suggestion.Threshold;
        SuggestionText = suggestion.GapTooNarrow
            ? $"Suggested: {suggestion.Threshold:F0} — but your voice barely clears the game. Lower the game volume or move the mic closer, then re-measure."
            : $"Suggested threshold: {suggestion.Threshold:F0}";
    }

    private void OnFrameMeasured(double energy, bool inSpeech)
    {
        lock (_measureGate) _measureSamples?.Add(energy);

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            CurrentEnergy = energy;
            if (energy > PeakEnergy) PeakEnergy = energy;
            TunerState = inSpeech ? "SPEECH" : "quiet";
        });
    }

    private void OnUtteranceEnded(bool longEnough, int durationMs)
    {
        var seconds = durationMs / 1000.0;
        Log(longEnough
            ? $"✓ would send a {seconds:F1}s utterance to Claude"
            : $"✗ {seconds:F1}s blip ignored (under min speech)");
    }

    private void Log(string line)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            TuningLog.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
            while (TuningLog.Count > 8)
                TuningLog.RemoveAt(TuningLog.Count - 1);
        });
    }

    // Threshold/endpoint/min-speech edits apply to the running meter immediately.
    partial void OnSilenceThresholdChanged(double value) => PushSettingsToTuner();
    partial void OnEndpointSilenceMsChanged(int value) => PushSettingsToTuner();
    partial void OnMinSpeechMsChanged(int value) => PushSettingsToTuner();

    private void PushSettingsToTuner()
    {
        if (IsTuning)
            _tuner.UpdateSettings(SilenceThreshold, EndpointSilenceMs, MinSpeechMs);
    }

    public void Save()
    {
        _settings.VoiceMode = SelectedMode;
        _settings.SilenceThreshold = SilenceThreshold;
        _settings.EndpointSilenceMs = EndpointSilenceMs;
        _settings.MinSpeechMs = MinSpeechMs;
        _settings.Effort = Effort;
        _settings.TtsModel = TtsModel;
        SettingsStore.Save(_settingsPath, _settings);

        var sources = Sites
            .Where(s => !string.IsNullOrWhiteSpace(s.Url))
            .Select(s => new ReferenceSource(
                string.IsNullOrWhiteSpace(s.Name) ? s.Url.Trim() : s.Name.Trim(),
                s.Url.Trim()))
            .ToList();

        ReferenceConfig.Save(_referencePath, new ReferenceOptions
        {
            Sources = sources.Count > 0 ? sources : ReferenceOptions.DefaultSources,
            MaxCharsReturned = MaxReferenceChars <= 0 ? 1500 : MaxReferenceChars,
        });
    }
}
