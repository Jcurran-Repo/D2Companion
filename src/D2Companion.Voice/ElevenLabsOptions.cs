namespace D2Companion.Voice;

/// <summary>ElevenLabs configuration. Key + voice come from user-secrets at runtime.</summary>
public sealed class ElevenLabsOptions
{
    public required string ApiKey { get; init; }

    /// <summary>The ElevenLabs voice id Claude speaks with.</summary>
    public required string VoiceId { get; init; }

    /// <summary>Speech-to-text model. Scribe is ElevenLabs' STT.</summary>
    public string SttModel { get; init; } = "scribe_v1";

    /// <summary>Text-to-speech model. Turbo is low-latency, good for a live companion.</summary>
    public string TtsModel { get; init; } = "eleven_turbo_v2_5";
}
