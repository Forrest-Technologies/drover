namespace Drover.Ui.Pages;

/// <summary>
/// What the rail beside the main conversation shows: nothing, the conversation's summary, what the bot remembers about
/// the person, the bot's screen, the files exchanged with the bot, a run's activity, or its side chats.
/// </summary>
public enum SideView
{
    Closed,
    Summary,
    Memory,
    Screen,
    Activity,
    Files,
    List,
    New,
    Chat,
    Work,
}
