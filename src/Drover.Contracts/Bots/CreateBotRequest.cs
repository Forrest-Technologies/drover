namespace Drover.Contracts.Bots;

/// <summary>
/// A new bot: a name, what it should do, and the model it talks to. It goes beneath <see cref="ParentBotId"/>, a bot
/// of the same space, or at the top of the space's tree when that is null.
/// </summary>
public sealed record CreateBotRequest(
    string? Name,
    string? Instructions,
    Guid ModelConnectionId,
    string? Model,
    Guid? ParentBotId = null);
