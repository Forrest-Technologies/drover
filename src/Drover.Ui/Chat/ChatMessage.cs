namespace Drover.Ui.Chat;

/// <summary>
/// One message as the conversation shows it. <see cref="Streaming"/> is true while a bot's text is still arriving.
/// <see cref="Pending"/> is true for a person's message sent while the bot was working that the bot has not read yet.
/// <see cref="Tools"/> are the bot's tool calls that follow its words, in the order it made them.
/// </summary>
public sealed record ChatMessage(string Key, bool FromPerson, string Text, bool Streaming = false, bool Pending = false)
{
    public IReadOnlyList<ToolCard> Tools { get; init; } = [];
}
