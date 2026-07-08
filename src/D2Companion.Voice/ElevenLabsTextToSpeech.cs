using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace D2Companion.Voice;

/// <summary>Turns Claude's reply text into spoken MP3 audio via the ElevenLabs text-to-speech API.</summary>
public sealed class ElevenLabsTextToSpeech : IDisposable
{
    private const string BaseUrl = "https://api.elevenlabs.io/v1/text-to-speech/";

    private readonly HttpClient _http = new();
    private readonly ElevenLabsOptions _options;

    public ElevenLabsTextToSpeech(ElevenLabsOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http.DefaultRequestHeaders.Add("xi-api-key", options.ApiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));
    }

    /// <summary>Synthesizes speech and returns MP3 bytes.</summary>
    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<byte>();

        var payload = JsonSerializer.Serialize(new { text, model_id = _options.TtsModel });
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + _options.VoiceId)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public void Dispose() => _http.Dispose();
}
