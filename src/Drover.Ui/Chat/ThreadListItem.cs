namespace Drover.Ui.Chat;

/// <summary>
/// One row of the thread list: the thread, the bot it is with, and whether that bot is working in it now.
/// </summary>
public sealed record ThreadListItem(Guid Id, string Title, string BotName, int BotHue, bool Working);
