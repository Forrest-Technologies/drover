namespace Drover.Adapters.Mcp;

/// <summary>
/// What a failed exchange with a tool server means, in words for a person. A message never repeats what the server
/// or the library said: that text is a third party's, and may echo a request header.
/// </summary>
public static class Failures
{
    #region Public Methods

    public static ToolSourceException Translate(Exception failure, bool timedOut)
    {
        if (Find<OutboundBlockedException>(failure) is { } blocked)
        {
            return new ToolSourceException(ToolSourceFailure.Blocked, blocked.Message, failure);
        }

        if (Find<ToolResponseTooLargeException>(failure) is not null)
        {
            return new ToolSourceException(ToolSourceFailure.TooLarge, "The tool server's answer was larger than this server accepts.", failure);
        }

        if (timedOut || Find<TimeoutException>(failure) is not null)
        {
            return new ToolSourceException(ToolSourceFailure.TimedOut, "The tool server did not answer in time.", failure);
        }

        if (Find<HttpRequestException>(failure) is { } request)
        {
            return request.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ToolSourceException(
                    ToolSourceFailure.Unauthorized,
                    "The tool server refused the credential. Check the connection's sign-in or header.",
                    failure),
                null => new ToolSourceException(ToolSourceFailure.Unreachable, "The tool server could not be reached.", failure),
                _ => new ToolSourceException(
                    ToolSourceFailure.ServerError,
                    $"The tool server answered with an error (HTTP {(int)request.StatusCode.Value}).",
                    failure),
            };
        }

        if (IsProtocolMismatch(failure))
        {
            return new ToolSourceException(
                ToolSourceFailure.ProtocolNotSupported,
                "The tool server does not speak a protocol version this connection allows.",
                failure);
        }

        return failure is McpException
            ? new ToolSourceException(ToolSourceFailure.ServerError, "The tool server reported an error for this request.", failure)
            : new ToolSourceException(ToolSourceFailure.Unreachable, "The tool server could not be used.", failure);
    }

    public static bool IsProtocolMismatch(Exception failure) =>
        Find<McpProtocolException>(failure) is { ErrorCode: McpErrorCode.UnsupportedProtocolVersion or McpErrorCode.MethodNotFound };

    /// <summary>
    /// True when a server that was reached and answered would not open with the one protocol revision it was asked
    /// for. The library reports that as its own failure with no transport failure behind it.
    /// </summary>
    public static bool IsRefusalOfTheProtocol(Exception failure) =>
        IsProtocolMismatch(failure)
        || (Find<McpException>(failure) is not null
            && Find<HttpRequestException>(failure) is null
            && Find<TimeoutException>(failure) is null
            && Find<OperationCanceledException>(failure) is null);

    #endregion

    #region Private Methods

    private static T? Find<T>(Exception? failure)
        where T : Exception =>
        failure switch
        {
            null => null,
            T found => found,
            AggregateException many => many.InnerExceptions.Select(Find<T>).FirstOrDefault(found => found is not null),
            _ => Find<T>(failure.InnerException),
        };

    #endregion
}
