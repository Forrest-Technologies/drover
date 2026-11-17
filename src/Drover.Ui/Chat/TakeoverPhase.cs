namespace Drover.Ui.Chat;

/// <summary>Where a bot's request for a person at its screen stands.</summary>
public enum TakeoverPhase
{
    Asked,
    Holding,
    Done,
    CouldNot,
}
