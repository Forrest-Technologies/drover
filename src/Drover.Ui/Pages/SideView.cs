namespace Drover.Ui.Pages;

/// <summary>
/// What the rail beside the main conversation shows: nothing, the conversation's summary, what the bot remembers about
/// the person, the bot's screen, or its side chats.
/// </summary>
public enum SideView
{
    Closed,
    Summary,
    Memory,
    Screen,
    List,
    New,
    Chat,
}
