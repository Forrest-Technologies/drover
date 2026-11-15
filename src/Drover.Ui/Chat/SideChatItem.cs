namespace Drover.Ui.Chat;

/// <summary>
/// One row of a bot's side chats: the chat, where it opens, the bot it is with, and whether that bot is working in
/// it now.
/// </summary>
public sealed record SideChatItem(Guid Id, string Title, string Href, string BotName, int BotHue, bool Working);
