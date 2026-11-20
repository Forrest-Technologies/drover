namespace Drover.Ui.Pages;

/// <summary>Which way in the conversation's header opens each thing beside it.</summary>
public static class SideViewWays
{
    #region Public Methods

    /// <summary>
    /// The way in the conversation's header that opens <paramref name="side"/>: the side chats for one side chat,
    /// and none for work handed to another bot, which opens from its card in the conversation.
    /// </summary>
    public static SideView? Way(this SideView side) => side switch
    {
        SideView.New or SideView.Chat => SideView.List,
        SideView.Work or SideView.Closed => null,
        _ => side,
    };

    #endregion
}
