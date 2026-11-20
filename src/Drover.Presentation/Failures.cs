namespace Drover.Ui;

/// <summary>
/// Says a failed request in words a person can act on.
/// </summary>
public static class Failures
{
    #region Properties

    public const string Unreachable = "Drover could not reach its server. Check that it is running.";

    public const string TooSlow = "Drover's server did not answer in time.";

    public const string Unreadable = "Drover's server sent an answer this page could not read.";

    #endregion

    #region Public Methods

    /// <summary>
    /// True for the failures a screen reports in place: the server refused, could not be reached, did not answer
    /// before the client gave up, or answered with something that is not what was asked for, such as a page a proxy
    /// put in front of it.
    /// </summary>
    public static bool IsReportable(Exception exception) => exception is DroverApiException or HttpRequestException or JsonException || IsTimeout(exception);

    /// <summary>
    /// True when the server could not be reached, timed out, returned a server error, or sent an answer the client
    /// could not read. Client refusals (such as 401, 403, or 404) mean the server answered and are left to the
    /// screen.
    /// </summary>
    public static bool IsUnreachable(Exception exception) => exception switch
    {
        HttpRequestException request => request.StatusCode is null or >= HttpStatusCode.InternalServerError,
        DroverApiException refusal => refusal.StatusCode is < HttpStatusCode.BadRequest or >= HttpStatusCode.InternalServerError,
        JsonException => true,
        _ => IsTimeout(exception),
    };

    /// <summary>
    /// The server's own sentence when it refused for a reason the person can fix. A failure of the server itself,
    /// a rate limit, or a lost session gets a sentence from here, because those carry none worth showing.
    /// </summary>
    public static string Sentence(Exception exception) => exception switch
    {
        DroverApiException refusal => refusal.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => "Too many attempts. Wait a minute and try again.",
            >= HttpStatusCode.InternalServerError => "The server failed to answer. Try again, and check the server log if it keeps happening.",
            HttpStatusCode.Forbidden when IsGeneric(refusal) => "Your account is not allowed to do that.",
            HttpStatusCode.Unauthorized when IsGeneric(refusal) => "You are signed out. Sign in and try again.",
            _ => refusal.Message,
        },
        HttpRequestException => Unreachable,
        _ when IsTimeout(exception) => TooSlow,
        _ => Unreadable,
    };

    #endregion

    #region Private Methods

    // How the client's own time limit ends a request: cancelled, because of a timeout, and not by the screen.
    private static bool IsTimeout(Exception exception) => exception is TaskCanceledException { InnerException: TimeoutException };

    // The client writes this when the server sent no sentence of its own.
    private static bool IsGeneric(DroverApiException refusal) =>
        refusal.Message.StartsWith("The server refused the request", StringComparison.Ordinal);

    #endregion
}
