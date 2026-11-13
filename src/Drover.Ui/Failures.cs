namespace Drover.Ui;

/// <summary>
/// Says a failed request in words a person can act on.
/// </summary>
public static class Failures
{
    #region Properties

    public const string Unreachable = "Drover could not reach its server. Check that it is running, then try again.";

    #endregion

    #region Public Methods

    /// <summary>True for the failures a screen reports in place: the server refused, or could not be reached.</summary>
    public static bool IsReportable(Exception exception) => exception is DroverApiException or HttpRequestException;

    /// <summary>
    /// The server's own sentence when it refused for a reason the person can fix. A failure of the server itself,
    /// a rate limit, or a lost session gets a sentence from here, because those carry none worth showing.
    /// </summary>
    public static string Sentence(Exception exception) => exception is not DroverApiException refusal
        ? Unreachable
        : refusal.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => "Too many attempts. Wait a minute and try again.",
            >= HttpStatusCode.InternalServerError => "The server failed to answer. Try again, and check the server log if it keeps happening.",
            HttpStatusCode.Forbidden when IsGeneric(refusal) => "Your account is not allowed to do that.",
            HttpStatusCode.Unauthorized when IsGeneric(refusal) => "You are signed out. Sign in and try again.",
            _ => refusal.Message,
        };

    #endregion

    #region Private Methods

    // The client writes this when the server sent no sentence of its own.
    private static bool IsGeneric(DroverApiException refusal) =>
        refusal.Message.StartsWith("The server refused the request", StringComparison.Ordinal);

    #endregion
}
