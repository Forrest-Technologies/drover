namespace Drover.Contracts.Bots;

/// <summary>A new bot at the top of the space's tree: a name, what it should do, and the model it talks to.</summary>
public sealed record CreateBotRequest(string? Name, string? Instructions, Guid ModelConnectionId, string? Model);
