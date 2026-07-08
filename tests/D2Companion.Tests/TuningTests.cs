using D2Companion.Voice;
using Xunit;

namespace D2Companion.Tests;

public sealed class TuningTests
{
    [Fact]
    public void FrameEnergy_SilenceIsZero()
    {
        Assert.Equal(0, FrameEnergy.Of(new byte[320]));
        Assert.Equal(0, FrameEnergy.Of(Array.Empty<byte>()));
    }

    [Fact]
    public void FrameEnergy_IsMeanAbsoluteAmplitude()
    {
        // Two samples: +1000 and -1000 → mean |amplitude| = 1000.
        var buffer = new byte[4];
        BitConverter.GetBytes((short)1000).CopyTo(buffer, 0);
        BitConverter.GetBytes((short)-1000).CopyTo(buffer, 2);

        Assert.Equal(1000, FrameEnergy.Of(buffer));
    }

    [Fact]
    public void Percentile_PicksNearestRank()
    {
        var samples = new double[] { 10, 20, 30, 40, 50 };

        Assert.Equal(50, TuningMath.Percentile(samples, 0.99));
        Assert.Equal(30, TuningMath.Percentile(samples, 0.5));
        Assert.Equal(0, TuningMath.Percentile(Array.Empty<double>(), 0.5));
    }

    [Fact]
    public void SuggestThreshold_SitsBetweenNoiseAndSpeech()
    {
        // Noisy game at 600, clear voice at 2200 — plenty of gap.
        var s = TuningMath.SuggestThreshold(noisePeak: 600, speechTypical: 2200);

        Assert.False(s.GapTooNarrow);
        Assert.InRange(s.Threshold, 700, 1100); // above noise, well under speech
        Assert.Equal(0, s.Threshold % 25);      // rounded for a tidy settings value
    }

    [Fact]
    public void SuggestThreshold_QuietRoomStillGetsASaneFloor()
    {
        // Nearly silent room: don't suggest a hair-trigger threshold.
        var s = TuningMath.SuggestThreshold(noisePeak: 40, speechTypical: 1800);

        Assert.True(s.Threshold >= 250);
        Assert.False(s.GapTooNarrow);
    }

    [Fact]
    public void SuggestThreshold_FlagsWhenGameNearlyDrownsTheVoice()
    {
        var s = TuningMath.SuggestThreshold(noisePeak: 1500, speechTypical: 1900);

        Assert.True(s.GapTooNarrow);
        Assert.True(s.Threshold > 1500); // still above the noise it measured
    }
}
