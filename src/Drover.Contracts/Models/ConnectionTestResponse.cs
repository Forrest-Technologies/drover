namespace Drover.Contracts.Models;

/// <summary>
/// The outcome of testing a connection, in plain words. <see cref="Failure"/> is one of invalid_key, retry_later,
/// out_of_credit, unreachable, rejected, or null on success.
/// </summary>
public sealed record ConnectionTestResponse(bool Succeeded, string Message, string? Failure, ModelConnectionSummary Connection);
