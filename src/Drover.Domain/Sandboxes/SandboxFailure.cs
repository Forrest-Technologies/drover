namespace Drover.Domain.Sandboxes;

/// <summary>Why a sandbox provider call failed, in the terms the broker acts on.</summary>
public enum SandboxFailure
{
    /// <summary>The operator's provider key is missing, malformed, or revoked.</summary>
    InvalidKey,

    /// <summary>The sandbox, or the file or folder asked for, does not exist.</summary>
    NotFound,

    /// <summary>The provider's record names another space or sharing group than the handle. Nothing was done.</summary>
    HandleMismatch,

    /// <summary>The sandbox refused the handle's access token.</summary>
    Unauthorized,

    /// <summary>The sandbox exists but is paused or stopped. Resuming it and trying again can work.</summary>
    NotRunning,

    /// <summary>The sandbox is changing state (starting, pausing, resuming) or another request for it is in flight. Trying again shortly can work.</summary>
    Conflict,

    /// <summary>The provider account is not allowed to do this: suspended, blocked, or without access to the feature. Trying again changes nothing.</summary>
    Forbidden,

    /// <summary>The provider cannot do this right now, but may shortly.</summary>
    RetryLater,

    /// <summary>The provider is limiting requests.</summary>
    RateLimited,

    /// <summary>The provider account is at a plan or spend limit.</summary>
    QuotaExceeded,

    /// <summary>The provider refused the request for another reason. Trying again changes nothing.</summary>
    Rejected,

    /// <summary>The content is larger than the provider or the sandbox's disk accepts.</summary>
    TooLarge,

    /// <summary>The command was still running at its time limit and was stopped.</summary>
    TimedOut,

    /// <summary>The provider could not be reached, failed on its side, or answered in a form Drover does not read.</summary>
    Unavailable,
}
