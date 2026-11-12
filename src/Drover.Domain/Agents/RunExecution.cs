namespace Drover.Domain.Agents;

/// <summary>Everything the runtime needs to run one step of a run, all in Drover's own types.</summary>
public sealed record RunExecution
{
    #region Properties

    public required Guid RunId { get; init; }

    public required Guid SpaceId { get; init; }

    public required Guid ThreadId { get; init; }

    /// <summary>The human the run acts for. Every read and write of the run's data is made as this user.</summary>
    public required Guid PrincipalUserId { get; init; }

    public required BotDefinition Bot { get; init; }

    /// <summary>The granted tools only. A tool with no grant is never in this list, so it never reaches the model.</summary>
    public required IReadOnlyList<ToolDefinition> Tools { get; init; }

    /// <summary>
    /// The model the bot talks to, already built for the bot's connection. The engine owns it: it counts steps and
    /// records usage around every call, and disposes it when the step ends.
    /// </summary>
    public IModelChatClient? Model { get; init; }

    /// <summary>Answers to whatever the run was waiting on.</summary>
    public IReadOnlyList<PendingResponse> Responses { get; init; } = [];

    #endregion
}
