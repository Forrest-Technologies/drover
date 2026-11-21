namespace Drover.Contracts.Bots;

/// <summary>
/// A new bot: a name, what it should do, and the model it talks to. It goes beneath <see cref="ParentBotId"/>, a bot
/// of the same space, or at the top of the space's tree when that is null. <see cref="RequestId"/> is chosen by the
/// client once for the bot it is making: if the answer is lost, sending the request again with it returns the bot that
/// was made and never a second one. It names the request, not the bot: the server gives the bot its own id, and the
/// same request id in another space, or from another person, is a different request. A client that leaves it out gets
/// no such guarantee.
/// </summary>
public sealed record CreateBotRequest(
    string? Name,
    string? Instructions,
    Guid ModelConnectionId,
    string? Model,
    Guid? ParentBotId = null,
    Guid? RequestId = null);
