using NAudio.Wave;

namespace D2Companion.Voice;

/// <summary>
/// The measurement half of open-mic tuning: listens to the mic with the SAME frame size,
/// energy metric, and <see cref="VoiceActivityDetector"/> as <see cref="OpenMicListener"/>,
/// but keeps no audio and calls no APIs — it only reports what real listening WOULD do.
/// Settings can be swapped live so threshold changes take effect mid-meter.
/// </summary>
public sealed class MicTuner : IDisposable
{
    /// <summary>Raised per 40ms frame (off the UI thread): the frame's energy and whether
    /// the detector currently considers the mic to be in speech.</summary>
    public event Action<double, bool>? FrameMeasured;

    /// <summary>Raised when an utterance ends (off the UI thread): whether it was long
    /// enough to count, and its approximate duration in milliseconds.</summary>
    public event Action<bool, int>? UtteranceEnded;

    private const int FrameMs = 40;
    private readonly WaveFormat _format = new(16000, 16, 1);
    private readonly object _gate = new();

    private WaveInEvent? _waveIn;
    private VoiceActivityDetector? _vad;
    private int _endpointSilenceMs;
    private DateTime _speechStartedUtc;

    public bool IsRunning
    {
        get { lock (_gate) return _waveIn is not null; }
    }

    public void Start(double threshold, int endpointSilenceMs, int minSpeechMs)
    {
        lock (_gate)
        {
            if (_waveIn is not null) return;
            _vad = new VoiceActivityDetector(threshold, FrameMs, endpointSilenceMs, minSpeechMs);
            _endpointSilenceMs = endpointSilenceMs;
            _waveIn = new WaveInEvent { WaveFormat = _format, BufferMilliseconds = FrameMs };
            _waveIn.DataAvailable += OnData;
            _waveIn.StartRecording();
        }
    }

    /// <summary>Applies new settings to the running meter (no-op when stopped). The
    /// detector restarts cleanly, so mid-utterance state is discarded.</summary>
    public void UpdateSettings(double threshold, int endpointSilenceMs, int minSpeechMs)
    {
        lock (_gate)
        {
            if (_waveIn is null) return;
            _vad = new VoiceActivityDetector(threshold, FrameMs, endpointSilenceMs, minSpeechMs);
            _endpointSilenceMs = endpointSilenceMs;
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
            _vad = null;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        double energy;
        bool inSpeech;
        VadSignal signal;
        int durationMs = 0;

        lock (_gate)
        {
            if (_vad is null) return;

            var frame = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, frame, e.BytesRecorded);

            energy = FrameEnergy.Of(frame);
            signal = _vad.Feed(energy);
            inSpeech = _vad.InSpeech;

            if (signal == VadSignal.SpeechStarted)
                _speechStartedUtc = DateTime.UtcNow;
            else if (signal is VadSignal.SpeechEnded or VadSignal.Aborted)
            {
                // Gross time minus the endpoint tail ≈ how long the voiced part ran.
                var gross = (DateTime.UtcNow - _speechStartedUtc).TotalMilliseconds;
                durationMs = Math.Max(0, (int)(gross - _endpointSilenceMs));
            }
        }

        // Raise outside the lock, mirroring OpenMicListener.
        FrameMeasured?.Invoke(energy, inSpeech);
        if (signal is VadSignal.SpeechEnded or VadSignal.Aborted)
            UtteranceEnded?.Invoke(signal == VadSignal.SpeechEnded, durationMs);
    }

    public void Dispose() => Stop();
}
