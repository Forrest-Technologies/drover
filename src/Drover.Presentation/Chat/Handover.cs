namespace Drover.Ui.Chat;

/// <summary>
/// What a conversation needs to show work passing between bots: <see cref="BotName"/>, the bot whose messages these
/// are, the name of every bot in the crew by id, and the address of a child's side of a piece of work from the child
/// bot and its thread. <see cref="Depth"/> is how many handovers the messages sit inside: 0 for the conversation's
/// own bot.
/// </summary>
public sealed record Handover(
    string BotName,
    IReadOnlyDictionary<Guid, string> Names,
    Func<Guid, Guid, string>? WorkAddress = null,
    int Depth = 0,
    Guid BotId = default,
    Func<Guid, string>? LimitsAddress = null)
{
    #region Properties

    /// <summary>The name shown for a bot that is no longer in the crew.</summary>
    public const string Unknown = "A removed bot";

    #endregion

    #region Public Methods

    public string NameOf(Guid botId) => Names.GetValueOrDefault(botId) ?? Unknown;

    /// <summary>The same, one level down: for the messages of the bot that was handed the work.</summary>
    public Handover Below(string childName, Guid childId = default) =>
        this with { BotName = childName, Depth = Depth + 1, BotId = childId };

    #endregion
}
