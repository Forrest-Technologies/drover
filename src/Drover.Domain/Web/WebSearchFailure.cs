namespace Drover.Domain.Web;

/// <summary>Why a search could not be answered.</summary>
public enum WebSearchFailure
{
    /// <summary>The service is down, busy, slow, or could not be reached. Trying later may work.</summary>
    Unavailable,

    /// <summary>The service refused the request: its configuration does not allow it, or it asked for a key.</summary>
    Refused,

    /// <summary>The answer was not the shape the adapter reads.</summary>
    Malformed,

    /// <summary>The answer was larger than the request allows.</summary>
    TooLarge,

    /// <summary>This server's outbound policy refused the service's address before any request left.</summary>
    Blocked,
}
