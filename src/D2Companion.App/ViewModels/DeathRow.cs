using D2Companion.Core.Domain;

namespace D2Companion.App.ViewModels;

/// <summary>One entry in the death log panel.</summary>
public sealed class DeathRow
{
    public DeathRow(DeathRecord death)
    {
        Time = death.TimestampUtc.ToLocalTime().ToString("MM-dd HH:mm");
        var where = string.IsNullOrWhiteSpace(death.Act) ? $"{death.Difficulty}" : $"{death.Difficulty}, {death.Act}";
        Description = $"Level {death.Level} ({where}) — {death.Cause}";
    }

    public string Time { get; }
    public string Description { get; }
}
