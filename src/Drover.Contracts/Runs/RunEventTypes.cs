namespace Drover.Contracts.Runs;

/// <summary>The event types of docs/architecture/03-run-lifecycle.md, and how their payloads are written as JSON.</summary>
public static class RunEventTypes
{
    #region Private Fields

    // Event payloads are written and read with these options on both sides, so live and replay are the same bytes.
    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    #endregion

    #region Public Methods

    /// <summary>Reads an event's data as the payload record its type names.</summary>
    public static T Read<T>(RunEventResponse stored) => Read<T>(stored.Data);

    public static T Read<T>(string data) =>
        JsonSerializer.Deserialize<T>(data, json) ?? throw new JsonException("The event has no data.");

    /// <summary>Writes a payload record as the JSON text an event carries.</summary>
    public static string Write(object payload) => JsonSerializer.Serialize(payload, payload.GetType(), json);

    /// <summary>True for the events that end a run for good.</summary>
    public static bool IsTerminal(string type) => type is RunCompleted or RunFailed or RunCancelled;

    #endregion

    #region Types

    public const string RunQueued = "run.queued";

    public const string RunStarted = "run.started";

    public const string RunWaiting = "run.waiting";

    public const string RunCompleted = "run.completed";

    public const string RunFailed = "run.failed";

    public const string RunCancelled = "run.cancelled";

    public const string StepStarted = "step.started";

    public const string StepDiscarded = "step.discarded";

    public const string MessageDelta = "message.delta";

    public const string MessageCompleted = "message.completed";

    public const string ToolRequested = "tool.requested";

    public const string ToolApprovalRequested = "tool.approval_requested";

    public const string ToolStarted = "tool.started";

    /// <summary>A piece of a running tool's output. Data is <see cref="Drover.Contracts.Runs.ToolOutput"/>.</summary>
    public const string ToolOutput = "tool.output";

    public const string ToolCompleted = "tool.completed";

    public const string ToolApproved = "tool.approved";

    public const string ToolDenied = "tool.denied";

    public const string MessageDelivered = "message.delivered";

    public const string UsageRecorded = "usage.recorded";

    /// <summary>A bot handed a task to a bot beneath it, which now works as a child run. Data is <see cref="DelegationStarted"/>.</summary>
    public const string DelegationStarted = "delegation.started";

    /// <summary>The child run of a delegation ended and its answer, or why it has none, went back to the parent. Data is <see cref="DelegationCompleted"/>.</summary>
    public const string DelegationCompleted = "delegation.completed";

    /// <summary>A rule a person wrote on the bot applied. Data is <see cref="RuleFired"/>.</summary>
    public const string RuleFired = "rule.fired";

    /// <summary>A decision model checked a rule written as a sentence. Data is <see cref="RuleChecked"/>.</summary>
    public const string RuleChecked = "rule.checked";

    /// <summary>The configured memory service timed out or failed during recall; the run continues without it. Data is <see cref="MemoryServiceUnavailable"/>.</summary>
    public const string MemoryServiceUnavailable = "memory_service_unavailable";

    #endregion
}
