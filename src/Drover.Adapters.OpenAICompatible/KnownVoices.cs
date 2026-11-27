namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// What a model name says about speech, and the voices of well-known speech models. A compatible endpoint lists
/// names and nothing else, so these are the starting values: a space admin can mark any model by hand, and a
/// person can always type a voice name that is not listed here.
/// </summary>
internal static class KnownVoices
{
    #region Private Fields

    private static readonly string[] hostedSpeech =
        ["alloy", "ash", "ballad", "coral", "echo", "fable", "nova", "onyx", "sage", "shimmer", "verse", "marin", "cedar"];

    private static readonly string[] hostedRealtime =
        ["marin", "cedar", "alloy", "ash", "ballad", "coral", "echo", "sage", "shimmer", "verse"];

    private static readonly string[] kokoro =
    [
        "af_heart", "af_bella", "af_nicole", "af_aoede", "af_kore", "af_sarah", "af_nova", "af_sky", "af_alloy",
        "af_jessica", "af_river", "am_michael", "am_fenrir", "am_puck", "am_echo", "am_eric", "am_liam", "am_onyx",
        "am_adam", "bf_emma", "bf_isabella", "bf_alice", "bf_lily", "bm_george", "bm_fable", "bm_lewis", "bm_daniel",
    ];

    private static readonly string[] orpheus = ["autumn", "diana", "hannah", "austin", "daniel", "troy"];

    private static readonly string[] grok = ["eve", "ara", "leo", "rex", "sal"];

    #endregion

    #region Public Methods

    /// <summary>The voice used when nobody chose one. The first of the list a model offers.</summary>
    public static string DefaultFor(Uri address, string model) =>
        For(address, model) is [var first, ..] ? first.Id : "alloy";

    public static IReadOnlyList<VoiceOption> For(Uri address, string model)
    {
        var name = model.ToLowerInvariant();
        var ids = IsGrok(address) || name.StartsWith("grok", StringComparison.Ordinal)
            ? grok
            : name.Contains("kokoro", StringComparison.Ordinal)
                ? kokoro
                : name.Contains("orpheus", StringComparison.Ordinal)
                ? orpheus
                    : name.Contains("realtime", StringComparison.Ordinal)
                    ? hostedRealtime
                    : IsHostedSpeechName(name) ? hostedSpeech : [];
        return [.. ids.Select(id => new VoiceOption(id, id))];
    }

    /// <summary>The speech marks a model's name implies. Conservative: an unknown name gets none.</summary>
    public static ModelCapabilities Mark(ModelCapabilities capabilities, string model)
    {
        var name = model.ToLowerInvariant();
        var realtime = name.Contains("realtime", StringComparison.Ordinal) || name.StartsWith("grok-voice", StringComparison.Ordinal)
            && !name.Contains("transcribe", StringComparison.Ordinal);
        var transcribes = !realtime && (name.Contains("whisper", StringComparison.Ordinal)
            || name.Contains("transcribe", StringComparison.Ordinal)
            || name.Contains("speech-to-text", StringComparison.Ordinal)
            || name.Contains("stt", StringComparison.Ordinal)
            || name.Contains("parakeet", StringComparison.Ordinal));
        var speaks = !realtime && !transcribes && (name.Contains("tts", StringComparison.Ordinal)
            || name.Contains("kokoro", StringComparison.Ordinal)
            || name.Contains("orpheus", StringComparison.Ordinal)
            || name.Contains("phonon", StringComparison.Ordinal)
            || name.Contains("text-to-speech", StringComparison.Ordinal));

        return realtime || transcribes || speaks
            ? capabilities with
            {
                SpeechToText = transcribes,
                TextToSpeech = speaks,
                RealtimeVoice = realtime,

                // A speech model does not chat, so a bot is never offered it for tools.
                ToolCalling = false,
            }
            : capabilities;
    }

    public static bool IsGrok(Uri address) => address.IdnHost.EndsWith("x.ai", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for the service the realtime protocol comes from, which takes every setting the protocol has.</summary>
    public static bool IsHosted(Uri address) =>
        address.IdnHost.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase);

    #endregion

    #region Private Methods

    private static bool IsHostedSpeechName(string name) =>
        name.StartsWith("tts-", StringComparison.Ordinal) || name.Contains("-tts", StringComparison.Ordinal);

    #endregion
}
