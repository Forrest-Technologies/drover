namespace Drover.Presentation.Tests;

/// <summary>
/// Every branch of <see cref="Failures.IsUnreachable"/> decides whether the offline gate takes over or the screen
/// stays up with its own refusal.
/// </summary>
public sealed class FailuresTests
{
    #region Public Methods

    [Theory]
    [MemberData(nameof(Cases))]
    public void IsUnreachable_matches_only_connection_timeouts_server_errors_and_bad_json(
        Exception exception, bool expected) =>
        Failures.IsUnreachable(exception).Should().Be(expected);

    public static TheoryData<Exception, bool> Cases() => new()
    {
        { new HttpRequestException("Connection refused"), true },
        { new TaskCanceledException("Timed out", new TimeoutException("Timed out")), true },
        { new TaskCanceledException("Cancelled"), false },
        { new HttpRequestException("Server error", null, HttpStatusCode.InternalServerError), true },
        { new HttpRequestException("Unavailable", null, HttpStatusCode.ServiceUnavailable), true },
        { new HttpRequestException("Not found", null, HttpStatusCode.NotFound), false },
        { new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized), false },
        { new DroverApiException(HttpStatusCode.InternalServerError, "Server error."), true },
        { new DroverApiException(HttpStatusCode.ServiceUnavailable, "Unavailable."), true },
        { new DroverApiException(HttpStatusCode.Redirect, "Moved."), true },
        { new DroverApiException(HttpStatusCode.NotFound, "Not found."), false },
        { new DroverApiException(HttpStatusCode.Unauthorized, "Unauthorized."), false },
        { new JsonException("Unexpected token."), true },
        { new InvalidOperationException("App fault."), false },
    };

    #endregion
}
