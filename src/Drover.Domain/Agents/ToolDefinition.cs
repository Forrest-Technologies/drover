namespace Drover.Domain.Agents;

/// <summary>A granted tool as the runtime offers it to the model.</summary>
public sealed record ToolDefinition
{
    #region Properties

    public required Guid ToolId { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public required JsonElement InputSchema { get; init; }

    /// <summary>
    /// The hash of the definition this run was given. A call's row keeps it, so an approval covers the tool as it
    /// was when the call was asked for and no later version of it.
    /// </summary>
    public string? DefinitionHash { get; init; }

    public ToolSensitivity Sensitivity { get; init; } = ToolSensitivity.Write;

    /// <summary>The effective grant is <c>ask</c>: the run stops for a human before the tool runs.</summary>
    public bool RequiresApproval { get; init; }

    /// <summary>
    /// The built-in <c>delegate</c> tool: a call hands a task to a bot beneath this one, which works as its own run.
    /// </summary>
    public bool IsDelegation { get; init; }

    /// <summary>
    /// A built-in tool that Drover knows has the same effect run twice as once (<c>sandbox.share_file</c> finds the
    /// file it already shared). Never set for a tool server's tool.
    /// </summary>
    public bool IsIdempotent { get; init; }

    /// <summary>
    /// Whether a call cut off by a restart may run again. A read-only tool may: anything else might already have
    /// happened, so the model is told the outcome is unknown. A delegation may too, because asking again resolves to
    /// the same child run, which goes on from its own saved history and never starts over. So may an idempotent
    /// built-in tool, which finds what it already did.
    /// </summary>
    public bool MayRunAgainAfterInterruption =>
        IsDelegation || (IsIdempotent && !DefinedByServer) || (Sensitivity is ToolSensitivity.Read && !DefinedByServer);

    /// <summary>
    /// The tool comes from a tool server, so "it only reads" is the server's claim and not something Drover knows.
    /// Such a call is never repeated on the strength of that claim.
    /// </summary>
    public bool DefinedByServer { get; init; }

    #endregion
}
