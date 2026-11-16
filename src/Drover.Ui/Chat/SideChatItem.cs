namespace Drover.Ui.Chat;

/// <summary>
/// One row of a bot's side chats: the chat, where it opens, the bot it is with, whether that bot is working in it
/// now, and what the chat has for the person, so a word in the crew can always be traced to the chat it came from.
/// </summary>
public sealed record SideChatItem(
    Guid Id,
    string Title,
    string Href,
    string BotName,
    int BotHue,
    bool Working,
    Attention Attention = Attention.None);
