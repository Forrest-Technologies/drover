namespace Drover.Contracts.Projects;

/// <summary>A project's shared definition and the conversations the caller may see in it.</summary>
public sealed record ProjectResponse(
    Guid Id,
    string Name,
    string Instructions,
    IReadOnlyList<Guid> ConversationIds);

/// <summary>The shared project definition an owner or admin writes.</summary>
public sealed record ProjectBody(string? Name, string? Instructions);

/// <summary>Moves a conversation into one project, or out of every project when null.</summary>
public sealed record SetConversationProjectRequest(Guid? ProjectId);
