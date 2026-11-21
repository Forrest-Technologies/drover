namespace Drover.Contracts.Bots;

/// <summary>
/// A new bot: a name, what it should do, and the model it talks to. It goes beneath <see cref="ParentBotId"/>, a bot
/// of the same space, or at the top of the space's tree when that is null. <see cref="Id"/> is the id the new bot will
/// have, chosen by the client once for the bot it is making: if the answer is lost, sending the request again returns
/// the bot that was made for that id and never a second one. A client that leaves it out gets an id from the server and
/// no such guarantee.
/// </summary>
public sealed record CreateBotRequest(
    string? Name,
    string? Instructions,
    Guid ModelConnectionId,
    string? Model,
    Guid? ParentBotId = null,
    Guid? Id = null);
