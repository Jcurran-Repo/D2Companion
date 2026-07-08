namespace D2Companion.Voice;

/// <summary>What a fed audio frame implies about the utterance in progress.</summary>
public enum VadSignal
{
    None,
    SpeechStarted,
    SpeechEnded,
    /// <summary>An utterance ended but was too short to count (discard it).</summary>
    Aborted,
}

/// <summary>
/// A simple energy-threshold voice-activity detector with silence endpointing. Fed one frame
/// energy at a time, it decides when speech starts and when a pause is long enough to end the
/// turn — the "gap tolerance" that lets you pause mid-thought without being cut off. Kept free
/// of NAudio so the state machine is unit-testable.
/// </summary>
public sealed class VoiceActivityDetector
{
    private readonly double _threshold;
    private readonly int _endpointSilenceFrames;
    private readonly int _minSpeechFrames;

    private bool _inSpeech;
    private int _speechFrames;
    private int _silenceRun;

    public VoiceActivityDetector(double threshold, int frameMs, int endpointSilenceMs, int minSpeechMs)
    {
        _threshold = threshold;
        var frame = Math.Max(1, frameMs);
        _endpointSilenceFrames = Math.Max(1, endpointSilenceMs / frame);
        _minSpeechFrames = Math.Max(1, minSpeechMs / frame);
    }

    public bool InSpeech => _inSpeech;

    /// <summary>Feeds one frame's energy and returns what just happened.</summary>
    public VadSignal Feed(double energy)
    {
        var voiced = energy >= _threshold;

        if (!_inSpeech)
        {
            if (!voiced)
                return VadSignal.None;
            _inSpeech = true;
            _speechFrames = 1;
            _silenceRun = 0;
            return VadSignal.SpeechStarted;
        }

        if (voiced)
        {
            _speechFrames++;
            _silenceRun = 0;
            return VadSignal.None;
        }

        _silenceRun++;
        if (_silenceRun < _endpointSilenceFrames)
            return VadSignal.None;

        var enough = _speechFrames >= _minSpeechFrames;
        Reset();
        return enough ? VadSignal.SpeechEnded : VadSignal.Aborted;
    }

    private void Reset()
    {
        _inSpeech = false;
        _speechFrames = 0;
        _silenceRun = 0;
    }
}
