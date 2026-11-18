namespace Drover.Presentation.ViewModels;

/// <summary>The Team section of the bot editor: how far this bot may go when it builds bots of its own.</summary>
public static class TeamSectionFields
{
    #region Properties

    public static string Note => "A bot builds bots with its crew tools, which are set in the Tools section. These caps say how far it may go.";

    public static string ChildrenLabel => "Bots it may build";

    public static string DepthLabel => "Levels beneath it";

    public static string Placeholder => "The space's cap";

    #endregion
}
