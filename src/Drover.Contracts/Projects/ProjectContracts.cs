namespace Drover.Contracts.Projects;

/// <summary>A project of the current space, with its shared instructions.</summary>
public sealed record ProjectSummary(
    Guid Id,
    string Name,
    string Instructions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A new project: a non-blank name and optional shared instructions.</summary>
public sealed record CreateProjectRequest(string? Name, string? Instructions = null);

/// <summary>An edited project: a part left null keeps its stored value.</summary>
public sealed record UpdateProjectRequest(string? Name = null, string? Instructions = null);
