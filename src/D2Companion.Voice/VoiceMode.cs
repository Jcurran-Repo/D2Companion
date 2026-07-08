namespace D2Companion.Voice;

/// <summary>How the mic is driven.</summary>
public enum VoiceMode
{
    /// <summary>Hold a button to talk. Simple and robust with game audio.</summary>
    PushToTalk,

    /// <summary>Hands-free: listen continuously and respond on each detected utterance.</summary>
    OpenMic,
}
