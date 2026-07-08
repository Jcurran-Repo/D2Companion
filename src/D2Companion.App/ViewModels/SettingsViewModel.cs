using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
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
