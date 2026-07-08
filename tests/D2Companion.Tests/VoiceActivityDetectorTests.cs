using D2Companion.Voice;
using Xunit;

namespace D2Companion.Tests;

public sealed class VoiceActivityDetectorTests
{
    // 40ms frames, 200ms endpoint silence (5 frames), 120ms min speech (3 frames).
    private static VoiceActivityDetector New() => new(threshold: 500, frameMs: 40, endpointSilenceMs: 200, minSpeechMs: 120);

    [Fact]
    public void DetectsSpeechStartAndEndpoint()
    {
        var vad = New();

        Assert.Equal(VadSignal.None, vad.Feed(100));          // silence
        Assert.Equal(VadSignal.SpeechStarted, vad.Feed(900)); // speech begins
        Assert.Equal(VadSignal.None, vad.Feed(900));
        Assert.Equal(VadSignal.None, vad.Feed(900));          // 3 voiced frames >= min speech

        // Five silent frames = endpoint reached on the fifth.
        Assert.Equal(VadSignal.None, vad.Feed(50));
        Assert.Equal(VadSignal.None, vad.Feed(50));
        Assert.Equal(VadSignal.None, vad.Feed(50));
        Assert.Equal(VadSignal.None, vad.Feed(50));
        Assert.Equal(VadSignal.SpeechEnded, vad.Feed(50));
    }

    [Fact]
    public void ShortBlip_IsAborted()
    {
        var vad = New();

        Assert.Equal(VadSignal.SpeechStarted, vad.Feed(900)); // only 1 voiced frame (< 3 min)
        for (var i = 0; i < 4; i++)
            Assert.Equal(VadSignal.None, vad.Feed(50));
        Assert.Equal(VadSignal.Aborted, vad.Feed(50));        // endpoint, but too short → discard
    }

    [Fact]
    public void MidSpeechPause_DoesNotEndTurnEarly()
    {
        var vad = New();
        vad.Feed(900); // start
        vad.Feed(900);
        vad.Feed(900);

        // A brief pause shorter than the endpoint window must not end the turn...
        Assert.Equal(VadSignal.None, vad.Feed(50));
        Assert.Equal(VadSignal.None, vad.Feed(50));
        // ...and speech resuming resets the silence run.
        Assert.Equal(VadSignal.None, vad.Feed(900));
        Assert.False(vad.Feed(900) == VadSignal.SpeechEnded);
        Assert.True(vad.InSpeech);
    }
}
