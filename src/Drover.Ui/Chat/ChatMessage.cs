namespace Drover.Ui.Chat;

/// <summary>
/// One message as the conversation shows it. <see cref="Streaming"/> is true while a bot's text is still arriving.
/// <see cref="Pending"/> is true for a person's message sent while the bot was working that the bot has not read yet.
/// </summary>
public sealed record ChatMessage(string Key, bool FromPerson, string Text, bool Streaming = false, bool Pending = false);
