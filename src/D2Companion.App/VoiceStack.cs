using D2Companion.Voice;

namespace D2Companion.App;

/// <summary>Bundles the voice pieces so the view-model can drive a voice turn (push-to-talk or open-mic).</summary>
public sealed record VoiceStack(
    PushToTalkRecorder Recorder,
    ElevenLabsSpeechToText Stt,
    ElevenLabsTextToSpeech Tts,
    AudioPlayer Player,
    OpenMicListener OpenMic);
