using System.Net.Http.Headers;
using System.Text.Json;

namespace D2Companion.Voice;

/// <summary>Turns recorded WAV audio into text via the ElevenLabs speech-to-text API.</summary>
public sealed class ElevenLabsSpeechToText : IDisposable
{
    private const string Endpoint = "https://api.elevenlabs.io/v1/speech-to-text";

    private readonly HttpClient _http = new();
    private readonly ElevenLabsOptions _options;

    public ElevenLabsSpeechToText(ElevenLabsOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http.DefaultRequestHeaders.Add("xi-api-key", options.ApiKey);
    }

    /// <summary>Transcribes 16-bit PCM WAV audio and returns the recognized text (may be empty).</summary>
    public async Task<string> TranscribeAsync(byte[] wavAudio, CancellationToken cancellationToken = default)
    {
        if (wavAudio is null || wavAudio.Length == 0)
            return "";

        using var form = new MultipartFormDataContent();
        var audio = new ByteArrayContent(wavAudio);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "file", "speech.wav");
        form.Add(new StringContent(_options.SttModel), "model_id");

        using var response = await _http.PostAsync(Endpoint, form, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("text", out var text)
            ? text.GetString() ?? ""
            : "";
    }

    public void Dispose() => _http.Dispose();
}
