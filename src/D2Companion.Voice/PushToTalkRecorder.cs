using NAudio.Wave;

namespace D2Companion.Voice;

/// <summary>
/// Push-to-talk microphone capture. <see cref="Start"/> while the player holds the talk
/// key, <see cref="Stop"/> on release to get 16 kHz mono WAV bytes ready for STT. Because
/// the mic is only live while held, there are no silence gaps to endpoint and almost no
/// background game audio — the speech-input handling stays simple.
/// </summary>
public sealed class PushToTalkRecorder : IDisposable
{
    private readonly object _gate = new();
    private WaveInEvent? _waveIn;
    private MemoryStream? _stream;
    private WaveFileWriter? _writer;

    public bool IsRecording { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            if (IsRecording) return;

            _stream = new MemoryStream();
            _waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1) };
            _writer = new WaveFileWriter(_stream, _waveIn.WaveFormat);
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
            IsRecording = true;
        }
    }

    /// <summary>Stops capture and returns the recorded WAV bytes (empty if nothing was recorded).</summary>
    public byte[] Stop()
    {
        WaveInEvent? waveIn;
        lock (_gate)
        {
            if (!IsRecording) return Array.Empty<byte>();
            IsRecording = false;
            waveIn = _waveIn;
        }

        // StopRecording is asynchronous; wait for the final buffer to arrive.
        if (waveIn is not null)
        {
            using var stopped = new ManualResetEventSlim(false);
            void OnStopped(object? sender, StoppedEventArgs args) => stopped.Set();
            waveIn.RecordingStopped += OnStopped;
            waveIn.StopRecording();
            stopped.Wait(TimeSpan.FromSeconds(2));
            waveIn.RecordingStopped -= OnStopped;
        }

        lock (_gate)
        {
            _writer?.Dispose();                       // finalizes the WAV header, closes the stream
            var bytes = _stream?.ToArray() ?? Array.Empty<byte>(); // ToArray works on a closed MemoryStream
            _stream = null;
            _writer = null;
            _waveIn?.Dispose();
            _waveIn = null;
            return bytes;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_gate)
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        }
    }

    public void Dispose()
    {
        if (IsRecording)
        {
            try { Stop(); } catch { /* best-effort cleanup */ }
        }
    }
}
