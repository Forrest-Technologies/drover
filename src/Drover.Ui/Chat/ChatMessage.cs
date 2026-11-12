namespace Drover.Ui.Chat;

/// <summary>
/// One message as the conversation shows it. <see cref="Streaming"/> is true while a bot's text is still arriving.
/// </summary>
public sealed record ChatMessage(string Key, bool FromPerson, string Text, bool Streaming = false);
