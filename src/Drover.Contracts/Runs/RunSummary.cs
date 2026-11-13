namespace Drover.Contracts.Runs;

/// <summary>
/// A run as a row: which bot, which thread, how far it got, and what it cost. <see cref="Status"/> is queued,
/// running, waiting, completed, failed, or cancelled.
/// </summary>
public sealed record RunSummary(
    Guid Id,
    Guid ThreadId,
    Guid BotId,
    Guid RootRunId,
    string Status,
    string? StatusReason,
    RunProblem? Error,
    int Attempt,
    long LastEventSeq,
    UsageTotals Usage,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>Why a run failed, in words a person can read.</summary>
public sealed record RunProblem(string Kind, string Message);

/// <summary>Model usage: input, output, and cached input counts, and what they cost.</summary>
public sealed record UsageTotals(long Input, long Output, long Cached, decimal Cost);
