namespace Drover.Ui.Chat;

/// <summary>
/// Every word of voice: recording a message, reading a reply aloud, a live call and the Voice page. One place, so the
/// controls, the view models and their tests say the same thing.
/// </summary>
public static class VoiceWords
{
    #region Recording A Message

    public const string Record = "Record a message";

    public const string StopRecording = "Stop recording";

    public const string DiscardRecording = "Discard recording";

    public const string Recording = "Recording";

    public const string WritingItDown = "Writing it down";

    public const string NothingHeard = "No words were heard in the recording. Record it again.";

    public const string NoMicrophone = "This device has no microphone Drover can use. Connect one and try again.";

    public const string MicrophoneFailed = "The microphone stopped working. Check it and try again.";

    /// <summary>The microphone was refused, with where to allow it on this kind of device.</summary>
    public static string MicrophoneRefused(AudioHead head) => head switch
    {
        AudioHead.Browser => "Drover is not allowed to use the microphone. Allow it for this site in the browser's site settings, then try again.",
        AudioHead.Android => "Drover is not allowed to use the microphone. Allow it for Drover in Android's settings, under Apps and Permissions, then try again.",
        AudioHead.Apple => "Drover is not allowed to use the microphone. Allow it for Drover in Settings, under Privacy and Microphone, then try again.",
        _ => "Drover is not allowed to use the microphone. Allow it for Drover in the system's privacy settings, then try again.",
    };

    public static string CaptureFailed(AudioCaptureException failure, AudioHead head) => failure.Failure switch
    {
        AudioCaptureFailure.PermissionRefused => MicrophoneRefused(head),
        AudioCaptureFailure.NoMicrophone => NoMicrophone,
        _ => MicrophoneFailed,
    };

    /// <summary>The recording is larger than the server takes, said before it is sent, in the server's own words.</summary>
    public static string RecordingTooLong(long maxBytes) =>
        $"This recording is too long. This server takes recordings up to {maxBytes / (1024 * 1024)} MB.";

    #endregion

    #region Reading Aloud

    public const string ReadAloud = "Read aloud";

    public const string StopReading = "Stop reading";

    public const string CouldNotPlay = "This device could not play the sound. Check its sound output and try again.";

    #endregion

    #region A Live Call

    public const string Call = "Call";

    public const string Mute = "Mute";

    public const string Unmute = "Unmute";

    public const string ShowWords = "Show words";

    public const string HideWords = "Hide words";

    public const string HangUp = "Hang up";

    public const string ShrinkCall = "Shrink the call";

    public const string ExpandCall = "Expand the call";

    public const string CloseCall = "Close";

    public const string ConnectOne = "Connect one";

    public const string CallNotConnected = "The call could not be connected. Try again.";

    public const string CallDisconnected = "The call was disconnected. Call again to go on.";

    public const string ProviderEndedCall = "The speech provider ended the call. Call again to go on.";

    public const string YouSaid = "You said";

    public static string CallWith(string botName) => $"Call with {botName}";

    public static string BotSaid(string botName) => $"{botName} said";

    /// <summary>The one word for where a call stands. A call that never started has none.</summary>
    public static string State(CallState state) => state switch
    {
        CallState.Connecting => "Connecting",
        CallState.Listening => "Listening",
        CallState.Thinking => "Thinking",
        CallState.Speaking => "Speaking",
        CallState.Muted => "Muted",
        CallState.Ended => "Call ended",
        _ => string.Empty,
    };

    public static string TimeLimitReached(int? minutes) => minutes is { } limit
        ? $"The call reached this server's limit of {Minutes(limit)}. Call again to go on."
        : "The call reached this server's time limit. Call again to go on.";

    /// <summary>Minutes and seconds, as a clock shows them: 0:07, 12:40, 1:02:05.</summary>
    public static string Clock(TimeSpan elapsed) => elapsed.TotalHours >= 1
        ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
        : $"{elapsed.Minutes}:{elapsed.Seconds:00}";

    #endregion

    #region The Voice Page

    public const string PageTitle = "Voice";

    public const string ListensWith = "Listens with";

    public const string SpeaksWith = "Speaks with";

    public const string LiveCallsWith = "Live calls with";

    public const string Automatic = "Automatic";

    public const string OnYourNetwork = "On your network";

    public const string ThisServer = "This server";

    public const string VoiceLabel = "Voice";

    public const string PlaySample = "Play a sample";

    public const string StopSample = "Stop the sample";

    public const string SampleText = "This is a sample of this voice.";

    public const string NeedsModel = "Voice needs a speech model.";

    public const string Loading = "Loading the voice settings.";

    public const string CouldNotLoad = "Drover could not load the voice settings";

    public const string MembersRead = "An owner or admin of the space can change these.";

    public const string NotOnThisDevice = "Voice is not available in this app on this device yet. These settings still apply to the space.";

    public const string Advanced = "Advanced";

    public const string MarkByHand = "Mark a model for speech on Models";

    public const string NoLimit = "No limit";

    public const string NothingForSlot = "No connected model can do this.";

    /// <summary>What a slot uses now, with where it runs when that is worth saying.</summary>
    public static string Uses(VoiceSelection selection) =>
        $"{(selection.Automatic ? "Automatic: " : string.Empty)}{selection.Model}, {Where(selection.ConnectionName, selection.IsServer, selection.IsPrivateAddress)}";

    /// <summary>A model in a picker, under its connection's name.</summary>
    public static string Source(VoiceSourceResponse source) =>
        source.IsPrivateAddress ? $"{source.Model} ({OnYourNetwork})" : source.Model;

    public static string Where(string connectionName, bool isServer, bool isPrivateAddress) =>
        isServer ? ThisServer : isPrivateAddress ? $"{connectionName} ({OnYourNetwork})" : connectionName;

    public static string RecordingLimit(int? maxBytes) =>
        $"Longest recording: {(maxBytes is { } bytes ? $"{bytes / (1024 * 1024)} MB" : NoLimit)}";

    public static string ReadAloudLimit(int? maxCharacters) =>
        $"Longest text read aloud: {(maxCharacters is { } characters ? $"{characters.ToString("N0", CultureInfo.InvariantCulture)} characters" : NoLimit)}";

    public static string CallLimit(int? maxMinutes) =>
        $"Longest call: {(maxMinutes is { } minutes ? Minutes(minutes) : NoLimit)}";

    #endregion

    #region Failures

    /// <summary>
    /// A failed speech request in a sentence. The server's refusals here carry their own sentence, the provider's
    /// included, so it is shown as it is; anything else is said as any failed request is.
    /// </summary>
    public static string Sentence(Exception failure) => failure switch
    {
        DroverApiException refusal when HasOwnSentence(refusal.StatusCode) => refusal.Message,
        DeviceCallFailedException => CouldNotPlay,
        _ => Failures.Sentence(failure, "Try again."),
    };

    #endregion

    #region Private Methods

    private static bool HasOwnSentence(HttpStatusCode status) => status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound
        or HttpStatusCode.Conflict or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadGateway;

    private static string Minutes(int minutes) => minutes == 1 ? "1 minute" : $"{minutes} minutes";

    #endregion
}
