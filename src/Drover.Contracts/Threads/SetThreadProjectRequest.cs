namespace Drover.Contracts.Threads;

/// <summary>Moves a conversation into a project of the same space, or out of its project when <see cref="ProjectId"/> is null.</summary>
public sealed record SetThreadProjectRequest(Guid? ProjectId);
