namespace Drover.Ui.Tests.Support;

/// <summary>What a native shell registers: the server the app talks to. Counts how often a change was asked for.</summary>
public sealed class FakeServerChoice : IServerChoice
{
    #region Properties

    public int Changes { get; private set; }

    #endregion

    #region Interface Implementations

    public string Host => "drover.example.test";

    public Task Change()
    {
        Changes++;
        return Task.CompletedTask;
    }

    #endregion
}
