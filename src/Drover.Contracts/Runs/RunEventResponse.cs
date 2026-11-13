namespace Drover.Contracts.Runs;

/// <summary>
/// One event of a run tree. <see cref="Seq"/> is the gapless number within the root run; a client resumes with the
/// last one it saw. <see cref="Data"/> is the payload for <see cref="Type"/> as JSON text, exactly as stored: read it
/// with <see cref="RunEventTypes.Read{T}(RunEventResponse)"/> into the payload record the type names.
/// </summary>
public sealed record RunEventResponse(long Seq, Guid RunId, string Type, string Data, DateTimeOffset CreatedAt);
