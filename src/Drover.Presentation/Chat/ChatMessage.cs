namespace Drover.Ui.Chat;

/// <summary>
/// One message as the conversation shows it. <see cref="Streaming"/> is true while a bot's text is still arriving.
/// <see cref="Pending"/> is true for a person's message sent while the bot was working that the bot has not read yet.
/// <see cref="Tools"/> are the bot's tool calls that follow its words, in the order it made them. <see cref="Files"/>
/// are the files the message carries. <see cref="Reasoning"/> is what the bot reasoned before its words, when its model
/// shared that, and <see cref="Sources"/> are the sources its words cite.
/// <see cref="SentBy"/> says, in words, that a schedule sent the message for the person, who did not type it.
/// <see cref="RunId"/> is the run that wrote a bot's message; <see cref="EndsReply"/> is true on the last words of a
/// finished run when the reader can see its usage. <see cref="ActivityRunId"/> links the run's last stored reply to its activity.
/// <see cref="Model"/> is the model that wrote a bot's stored message, when the server kept it.
/// </summary>
public sealed record ChatMessage(string Key, bool FromPerson, string Text, bool Streaming = false, bool Pending = false)
{
    #region Properties

    public IReadOnlyList<ToolCard> Tools { get; init; } = [];

    public string? SentBy { get; init; }

    public IReadOnlyList<SharedFile> Files { get; init; } = [];

    public string? Reasoning { get; init; }

    public IReadOnlyList<SourceLink> Sources { get; init; } = [];

    /// <summary>True while the bot reasons and none of its words have arrived: the message says it is thinking.</summary>
    public bool Thinking => Streaming && Text.Length == 0 && Reasoning is not null;

    public Guid? RunId { get; init; }

    public bool EndsReply { get; init; }

    public Guid? ActivityRunId { get; init; }

    public string? Model { get; init; }

    #endregion
}
