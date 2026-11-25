namespace Drover.Domain.Runs;

/// <summary>Why a run failed, in words a person can read. Never carries a secret.</summary>
public sealed record RunError(string Code, string Message, RunErrorLimit? Limit = null);

/// <summary>The Drover limit a run stopped at: which limit, how much the run used, and the cap.</summary>
public sealed record RunErrorLimit(string Kind, long Used, long Cap);
