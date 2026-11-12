namespace Drover.Domain.Agents;

/// <summary>An answer to something a run was waiting on.</summary>
public abstract record PendingResponse;

/// <summary>A human's answer to an approval request, matched to it by the request id the runtime reported.</summary>
public sealed record ToolApproval(string RequestId, string CallId, bool Approved, string? Reason = null)
    : PendingResponse;
