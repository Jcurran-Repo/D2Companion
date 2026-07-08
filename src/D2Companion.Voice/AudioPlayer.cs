using NAudio.Wave;

namespace D2Companion.Voice;

/// <summary>Plays the MP3 audio ElevenLabs returns for Claude's spoken reply.</summary>
public sealed class AudioPlayer
{
    /// <summary>Plays MP3 bytes to completion (or until cancelled).</summary>
    public async Task PlayMp3Async(byte[] mp3, CancellationToken cancellationToken = default)
    {
        if (mp3 is null || mp3.Length == 0)
            return;

        using var source = new MemoryStream(mp3);
        using var reader = new Mp3FileReader(source);
        using var output = new WaveOutEvent();

        var finished = new TaskCompletionSource();
        output.PlaybackStopped += (_, _) => finished.TrySetResult();

        output.Init(reader);
        output.Play();

        await using (cancellationToken.Register(() =>
        {
            try { output.Stop(); } catch { /* stopping an already-stopped device */ }
        }))
        {
            await finished.Task;
        }
    }
}
