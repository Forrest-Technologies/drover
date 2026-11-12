namespace Drover.Domain.Policy;

/// <summary>
/// Raised when an outbound request is refused by policy because it would reach a private address. A refusal is not
/// a transient failure: it is never retried and says nothing about the health of the host.
/// </summary>
public sealed class OutboundBlockedException(string message) : HttpRequestException(message);
