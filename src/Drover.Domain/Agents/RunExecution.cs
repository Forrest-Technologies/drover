namespace Drover.Domain.Agents;

/// <summary>Everything the runtime needs to run one step of a run, all in Drover's own types.</summary>
public sealed record RunExecution
{
    public required Guid RunId { get; init; }

    public required Guid SpaceId { get; init; }

    public required Guid ThreadId { get; init; }

    /// <summary>The human the run acts for. Every read and write of the run's data is made as this user.</summary>
    public required Guid PrincipalUserId { get; init; }

    public required BotDefinition Bot { get; init; }

    /// <summary>The granted tools only. A tool with no grant is never in this list, so it never reaches the model.</summary>
    public required IReadOnlyList<ToolDefinition> Tools { get; init; }

    /// <summary>Answers to whatever the run was waiting on.</summary>
    public IReadOnlyList<PendingResponse> Responses { get; init; } = [];
}
