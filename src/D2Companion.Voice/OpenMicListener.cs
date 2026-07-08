using NAudio.Wave;

namespace D2Companion.Voice;

/// <summary>
/// Hands-free capture: listens continuously, uses <see cref="VoiceActivityDetector"/> to find
/// each utterance (with a short pre-roll so the start isn't clipped), and raises
/// <see cref="UtteranceCaptured"/> with the WAV. It auto-suspends after each utterance so it
/// doesn't hear Claude's own reply — the caller <see cref="Resume"/>s once the turn is done.
/// </summary>
public sealed class OpenMicListener : IDisposable
{
    /// <summary>Raised (off the UI thread) when a complete utterance is ready for STT.</summary>
    public event Action<byte[]>? UtteranceCaptured;

    private const int FrameMs = 40;
    private readonly WaveFormat _format = new(16000, 16, 1);
    private readonly double _threshold;
    private readonly int _endpointSilenceMs;
    private readonly int _minSpeechMs;
    private readonly int _preRollFrames;

    private readonly object _gate = new();
    private readonly Queue<byte[]> _preRoll = new();
    private WaveInEvent? _waveIn;
    private VoiceActivityDetector? _vad;
    private MemoryStream? _utterance;
    private volatile bool _suspended;

    public OpenMicListener(double silenceThreshold, int endpointSilenceMs, int minSpeechMs, int preRollMs = 200)
    {
        _threshold = silenceThreshold;
        _endpointSilenceMs = endpointSilenceMs;
        _minSpeechMs = minSpeechMs;
        _preRollFrames = Math.Max(0, preRollMs / FrameMs);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_waveIn is not null) return;
            _vad = NewDetector();
            _preRoll.Clear();
            _suspended = false;
            _waveIn = new WaveInEvent { WaveFormat = _format, BufferMilliseconds = FrameMs };
            _waveIn.DataAvailable += OnData;
            _waveIn.StartRecording();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_waveIn is null) return;
            _waveIn.DataAvailable -= OnData;
            try { _waveIn.StopRecording(); } catch { /* device already stopping */ }
            _waveIn.Dispose();
            _waveIn = null;
            _utterance?.Dispose();
            _utterance = null;
            _preRoll.Clear();
        }
    }

    /// <summary>Stop reacting to audio (e.g., while Claude speaks) without releasing the device.</summary>
    public void Suspend() => _suspended = true;

    /// <summary>Resume listening after a turn; clears any in-progress state.</summary>
    public void Resume()
    {
        lock (_gate)
        {
            _vad = NewDetector();
            _utterance?.Dispose();
            _utterance = null;
            _preRoll.Clear();
            _suspended = false;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (_suspended) return;

        byte[]? completed = null;
        lock (_gate)
        {
            if (_vad is null || _suspended) return;

            var frame = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, frame, e.BytesRecorded);

            switch (_vad.Feed(FrameEnergy.Of(frame)))
            {
                case VadSignal.SpeechStarted:
                    _utterance = new MemoryStream();
                    foreach (var pre in _preRoll)
                        _utterance.Write(pre, 0, pre.Length);
                    _preRoll.Clear();
                    _utterance.Write(frame, 0, frame.Length);
                    break;

                case VadSignal.None:
                    if (_utterance is not null)
                        _utterance.Write(frame, 0, frame.Length);
                    else
                        PushPreRoll(frame);
                    break;

                case VadSignal.SpeechEnded:
                    _utterance?.Write(frame, 0, frame.Length);
                    completed = FinalizeUtterance();
                    _suspended = true; // hold until the turn finishes, then Resume()
                    break;

                case VadSignal.Aborted:
                    _utterance?.Dispose();
                    _utterance = null;
                    break;
            }
        }

        // Raise outside the lock so the handler can call Suspend/Resume without deadlocking.
        if (completed is { Length: > 0 })
            UtteranceCaptured?.Invoke(completed);
    }

    private VoiceActivityDetector NewDetector() =>
        new(_threshold, FrameMs, _endpointSilenceMs, _minSpeechMs);

    private void PushPreRoll(byte[] frame)
    {
        _preRoll.Enqueue(frame);
        while (_preRoll.Count > _preRollFrames)
            _preRoll.Dequeue();
    }

    private byte[] FinalizeUtterance()
    {
        if (_utterance is null) return Array.Empty<byte>();
        var pcm = _utterance.ToArray();
        _utterance.Dispose();
        _utterance = null;

        using var wav = new MemoryStream();
        using (var writer = new WaveFileWriter(wav, _format))
            writer.Write(pcm, 0, pcm.Length);
        return wav.ToArray(); // ToArray works on the closed stream
    }

    public void Dispose() => Stop();
}
