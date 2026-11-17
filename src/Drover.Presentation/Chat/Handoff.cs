namespace Drover.Ui.Chat;

/// <summary>
/// Where work handed to a bot stands, as the crew column shows it on the connector to that bot: working on it, waiting
/// for a person, or come back to the bot that handed it over.
/// </summary>
public enum Handoff
{
    Working,
    Waiting,
    Returned,
}
