namespace D2Companion.Voice;

/// <summary>Outcome of a threshold calibration: the value to use and whether the gap
/// between game noise and speech was wide enough to trust it.</summary>
public sealed record ThresholdSuggestion(double Threshold, bool GapTooNarrow);

/// <summary>Pure math behind the settings tuner — kept device-free so it's unit-testable.</summary>
public static class TuningMath
{
    /// <summary>Value at percentile <paramref name="p"/> (0..1) of the samples (nearest-rank).</summary>
    public static double Percentile(IReadOnlyList<double> samples, double p)
    {
        if (samples.Count == 0) return 0;
        var sorted = samples.OrderBy(v => v).ToArray();
        var rank = (int)Math.Ceiling(Math.Clamp(p, 0, 1) * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }

    /// <summary>
    /// Suggests a silence threshold from the measured game-noise peak and typical speech
    /// energy: a quarter of the way from noise up to speech, floored so a silent room
    /// doesn't produce a hair-trigger. Flags the result when speech doesn't clear the
    /// noise by at least 50% — then the mic hears the game nearly as loudly as the player,
    /// and no threshold can separate them reliably.
    /// </summary>
    public static ThresholdSuggestion SuggestThreshold(double noisePeak, double speechTypical)
    {
        var gapTooNarrow = speechTypical < noisePeak * 1.5;
        var candidate = Math.Max(noisePeak * 1.2, noisePeak + 0.25 * (speechTypical - noisePeak));
        var floored = Math.Max(250, candidate);
        var rounded = Math.Round(floored / 25.0) * 25.0;
        return new ThresholdSuggestion(rounded, gapTooNarrow);
    }
}
