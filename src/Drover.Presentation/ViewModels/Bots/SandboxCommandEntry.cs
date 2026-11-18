namespace Drover.Presentation.ViewModels.Bots;

/// <summary>One command of this visit to a bot's sandbox, and what it left once it is done.</summary>
public sealed class SandboxCommandEntry(string command)
{
    #region Properties

    public string Command => command;

    public SandboxCommandResponse? Result { get; set; }

    public string? Problem { get; set; }

    #endregion
}
