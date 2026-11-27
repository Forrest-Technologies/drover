namespace Drover.Contracts.Voice;

/// <summary>
/// The text frames of a live call, as JSON with a <c>type</c>. Binary frames in both directions are audio: 16-bit
/// little-endian samples, one channel, 24,000 a second.
/// </summary>
public static class VoiceCallMessages
{
    #region Client To Server

    /// <summary>The person talked over the bot. <c>heardMs</c> says how much of the answer they heard.</summary>
    public const string Interrupt = "interrupt";

    public const string Hangup = "hangup";

    #endregion

    #region Server To Client

    public const string Ready = "ready";

    public const string PersonStarted = "person_started";

    public const string PersonSaid = "person_said";

    public const string BotSaying = "bot_saying";

    public const string BotSaid = "bot_said";

    public const string Failed = "failed";

    public const string Ended = "ended";

    #endregion

    #region Audio

    public const int SampleRate = 24_000;

    public const int BytesPerSample = 2;

    public const int Channels = 1;

    #endregion
}


/// <summary>Why a call ended, in the <c>reason</c> of the <c>ended</c> frame.</summary>
public static class VoiceCallEndReasons
{
    #region Reasons

    public const string HungUp = "hung_up";

    public const string TimeLimit = "time_limit";

    public const string Provider = "provider";

    public const string Server = "server";

    #endregion
}


/// <summary>
/// One text frame of a call. Only the members its <see cref="Type"/> uses are set: <c>ready</c> has the model, the
/// voice and the limit in minutes (null for none); the words frames have <see cref="Text"/>; <c>failed</c> has
/// <see cref="Message"/>; <c>ended</c> has <see cref="Reason"/>; <c>interrupt</c> has <see cref="HeardMs"/>.
/// </summary>
public sealed record VoiceCallFrame(string Type)
{
    #region Properties

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Voice { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxMinutes { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? HeardMs { get; init; }

    #endregion
}
