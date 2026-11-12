namespace Drover.Contracts.Bots;

/// <summary>A bot as lists and the chat screen show it.</summary>
public sealed record BotSummary(
    Guid Id,
    string Name,
    string Handle,
    string Instructions,
    Guid ModelConnectionId,
    string Model,
    string Status);
