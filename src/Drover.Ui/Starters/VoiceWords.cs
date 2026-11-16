namespace Drover.Ui.Starters;

/// <summary>Where speaking a message stands.</summary>
public enum VoicePhase
{
    Idle,
    Recording,
    Transcribing,
}

/// <summary>What stopped a recording from becoming words.</summary>
public enum VoiceProblem
{
    None,
    PermissionRefused,
    NoMicrophone,
    TooLong,
    ProviderFailed,
}

/// <summary>Speaking to a bot and hearing it, in words.</summary>
public static class VoiceWords
{
    #region Public Methods

    /// <summary>What the composer says while a message is being spoken. Read out as it changes.</summary>
    public static string Status(VoicePhase phase, TimeSpan elapsed) => phase switch
    {
        VoicePhase.Recording => $"Recording, {Words.Clock(elapsed)}",
        VoicePhase.Transcribing => "Writing down what you said.",
        _ => string.Empty,
    };

    /// <summary>What went wrong and what to do, or null when nothing did.</summary>
    public static string? Problem(VoiceProblem problem) => problem switch
    {
        VoiceProblem.PermissionRefused => "This browser is not allowed to use the microphone. Allow it in the browser's settings for this site, then try again.",
        VoiceProblem.NoMicrophone => "No microphone was found on this device.",
        VoiceProblem.TooLong => "That recording is longer than this server takes. Record a shorter one.",
        VoiceProblem.ProviderFailed => "The recording could not be written down. Try again, or type the message.",
        _ => null,
    };

    #endregion
}
