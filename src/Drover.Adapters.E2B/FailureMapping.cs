namespace Drover.Adapters.E2B;

/// <summary>Which side answered: the control plane (the API key) or the sandbox's daemon (the access token).</summary>
internal enum Plane
{
    ControlPlane,
    Sandbox,
}

/// <summary>
/// Turns E2B answers into <see cref="SandboxProviderException"/>. Messages are fixed text: the provider's own message
/// can name ids and paths, and nothing from a request (a key or token) is ever put in one.
/// </summary>
internal static class FailureMapping
{
    #region Public Methods

    public static SandboxProviderException FromError(ProviderError error, Plane plane)
    {
        if (error.Code is "idempotency_in_progress" || (error.ShouldRetry is true && error.Status is >= 400 and < 500))
        {
            return new(SandboxFailure.RetryLater, "The sandbox provider is busy with this request. Try again shortly.", error.RetryAfter);
        }

        return FromStatus(error.Status, plane, error.RetryAfter);
    }

    public static SandboxProviderException FromStatus(int status, Plane plane, TimeSpan? retryAfter = null) =>
        status switch
        {
            401 when plane == Plane.ControlPlane => new(
                SandboxFailure.InvalidKey,
                "The sandbox provider rejected the server's API key. Check the key in the server's sandbox settings."),
            401 or 403 when plane == Plane.Sandbox => new(
                SandboxFailure.Unauthorized,
                "The sandbox refused the access token, so nothing was done."),
            402 => new(SandboxFailure.QuotaExceeded, "The sandbox provider account is at a plan or spend limit."),
            404 => new(SandboxFailure.NotFound, "The sandbox provider has no such sandbox, file, or folder."),
            413 or 507 => new(SandboxFailure.TooLarge, "The content is larger than the sandbox accepts."),
            429 => new(
                SandboxFailure.RateLimited,
                "The sandbox provider is limiting requests right now. Try again in a moment.",
                retryAfter),
            503 => new(SandboxFailure.RetryLater, "The sandbox provider cannot do this right now. Try again shortly.", retryAfter),
            <= 0 or >= 500 => Unreachable(),
            _ => new(SandboxFailure.Rejected, $"The sandbox provider refused the request (HTTP {status})."),
        };

    /// <summary>A Connect RPC error code, from an error answer or the end of a stream, mapped to its HTTP status.</summary>
    public static SandboxProviderException FromConnectCode(string? code) =>
        code is "deadline_exceeded"
            ? TimedOut()
            : FromStatus(
                code switch
                {
                    "invalid_argument" or "failed_precondition" or "out_of_range" => 400,
                    "unauthenticated" => 401,
                    "permission_denied" => 403,
                    "not_found" => 404,
                    "already_exists" or "aborted" => 409,
                    "resource_exhausted" => 429,
                    "unavailable" => 503,
                    _ => 500,
                },
                Plane.Sandbox);

    public static SandboxProviderException Unreachable() =>
        new(SandboxFailure.Unavailable, "The sandbox provider could not be reached or failed on its side. Try again shortly.");

    public static SandboxProviderException Malformed() =>
        new(SandboxFailure.Unavailable, "The sandbox provider answered in a form Drover does not read.");

    public static SandboxProviderException EndedEarly() =>
        new(SandboxFailure.Unavailable, "The sandbox's answer ended before it was complete.");

    public static SandboxProviderException TimedOut() =>
        new(SandboxFailure.TimedOut, "The command was still running at its time limit, so it was stopped.");

    public static SandboxProviderException HandleMismatch() =>
        new(
            SandboxFailure.HandleMismatch,
            "The provider's record of this sandbox names another space or sharing group, so nothing was done.");

    public static SandboxProviderException Paused() =>
        new(SandboxFailure.Rejected, "The sandbox is paused. Resume it before using it.");

    public static SandboxProviderException MissingKey() =>
        new(SandboxFailure.InvalidKey, "No sandbox provider key is set. Add one in the server's sandbox settings.");

    /// <summary>A transport failure, a policy refusal, or a resilience refusal. Cancellation is never mapped.</summary>
    public static bool TryMapTransport(Exception exception, out SandboxProviderException mapped)
    {
        mapped = exception switch
        {
            OperationCanceledException => null!,
            SandboxProviderException failure => failure,
            OutboundBlockedException => new(
                SandboxFailure.Rejected,
                "This server does not allow connections to the sandbox provider's address."),
            BrokenCircuitException => new(
                SandboxFailure.RetryLater,
                "Calls to the sandbox provider are paused for a moment after repeated failures. Try again shortly."),
            TimeoutRejectedException or HttpRequestException or HttpIOException or IOException => Unreachable(),
            _ => null!,
        };

        return mapped is not null;
    }

    #endregion
}
