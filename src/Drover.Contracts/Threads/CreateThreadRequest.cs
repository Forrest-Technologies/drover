namespace Drover.Contracts.Threads;

public sealed record CreateThreadRequest(string? Title, Guid? ProjectId = null);
