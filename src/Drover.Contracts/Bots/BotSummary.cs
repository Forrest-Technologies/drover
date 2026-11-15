namespace Drover.Contracts.Bots;

/// <summary>
/// A bot as lists and the chat screen show it. <see cref="ParentBotId"/> places it in the space's tree: null for a
/// bot at the top.
/// </summary>
public sealed record BotSummary(
    Guid Id,
    string Name,
    string Handle,
    string Instructions,
    Guid ModelConnectionId,
    string Model,
    string Status,
    Guid? ParentBotId = null,
    Guid? MadeByBotId = null);
