namespace Drover.Domain.Runs;

/// <summary>Why a run failed, in words a person can read. Never carries a secret.</summary>
public sealed record RunError(string Code, string Message);
