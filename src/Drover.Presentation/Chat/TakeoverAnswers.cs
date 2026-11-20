namespace Drover.Ui.Chat;

/// <summary>
/// A person's two steps on a bot's request for them at its screen, sent to the server: taking it over, and handing it
/// back. Each returns why it failed, as a sentence, or nothing. The conversation and the screen beside it both send them.
/// </summary>
public static class TakeoverAnswers
{
    #region Properties

    /// <summary>The request was answered already, here or on another screen: nothing waits for this person any more.</summary>
    public const string AnsweredAlready = "This request was answered already.";

    #endregion

    #region Public Methods

    public static async Task<string?> TakeOver(this DroverApi api, Guid spaceId, Guid toolCallId, ILogger logger)
    {
        try
        {
            await api.TakeOverScreen(spaceId, toolCallId);
            return null;
        }
        catch (DroverApiException exception) when (exception.StatusCode is HttpStatusCode.Forbidden)
        {
            logger.LogInformation(exception, "Taking over the screen for tool call {ToolCallId} was refused", toolCallId);
            return "Owners and admins of this space use its bots' screens.";
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            return Failed(exception, toolCallId, "The screen was not opened.", logger);
        }
    }

    public static async Task<string?> HandBack(this DroverApi api, Guid spaceId, Guid toolCallId, bool done, string? note, ILogger logger)
    {
        try
        {
            await api.HandBackScreen(spaceId, toolCallId, done, string.IsNullOrWhiteSpace(note) ? null : note);
            return null;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            return Failed(exception, toolCallId, "Your answer was not sent.", logger);
        }
    }

    #endregion

    #region Private Methods

    private static string Failed(Exception exception, Guid toolCallId, string what, ILogger logger)
    {
        if (exception is DroverApiException { StatusCode: HttpStatusCode.Conflict })
        {
            logger.LogInformation(exception, "Tool call {ToolCallId} was answered already", toolCallId);
            return AnsweredAlready;
        }

        logger.LogWarning(exception, "A request about tool call {ToolCallId} failed", toolCallId);
        return $"{what} {Failures.Sentence(exception, "Check that the server is running.")}";
    }

    #endregion
}
