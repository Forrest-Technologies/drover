namespace Drover.Ui.Chat;

/// <summary>
/// One message as the conversation shows it. <see cref="Streaming"/> is true while a bot's text is still arriving.
/// <see cref="Pending"/> is true for a person's message sent while the bot was working that the bot has not read yet.
/// <see cref="Tools"/> are the bot's tool calls that follow its words, in the order it made them. <see cref="Files"/>
/// are the files the message carries.
/// <see cref="SentBy"/> says, in words, that a schedule sent the message for the person, who did not type it.
/// <see cref="RunId"/> is the run that wrote a bot's message; <see cref="EndsReply"/> is true on the last words of a
/// run that has finished, where what the reply used can be read.
/// </summary>
public sealed record ChatMessage(string Key, bool FromPerson, string Text, bool Streaming = false, bool Pending = false)
{
    #region Properties

    public IReadOnlyList<ToolCard> Tools { get; init; } = [];

    public string? SentBy { get; init; }

    public IReadOnlyList<SharedFile> Files { get; init; } = [];

    public Guid? RunId { get; init; }

    public bool EndsReply { get; init; }

    #endregion
}
