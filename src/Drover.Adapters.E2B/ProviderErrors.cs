namespace Drover.Adapters.E2B;

/// <summary>
/// Reads an error answer. The control plane sends <c>{"code":404,"message":...,"error_code":...}</c>; envd's Connect
/// RPC sends <c>{"code":"not_found","message":...}</c>; envd's file API and its proxy send a numeric code.
/// </summary>
internal static class ProviderErrors
{
    #region Private Fields

    private const int MaxErrorBytes = 16 * 1024;

    private const string ShouldRetryHeader = "Should-Retry";

    #endregion

    #region Public Methods

    public static async Task<SandboxProviderException> Read(
        HttpResponseMessage response,
        Plane plane,
        ILogger logger,
        CancellationToken cancellationToken) =>
        Map(await ReadError(response, logger, cancellationToken), plane);

    public static async Task<ProviderError> ReadError(
        HttpResponseMessage response,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var code = await ReadCode(response.Content, logger, cancellationToken);

        return new ProviderError(
            (int)response.StatusCode,
            code,
            ReadShouldRetry(response),
            response.Headers.RetryAfter?.Delta);
    }

    public static SandboxProviderException Map(ProviderError error, Plane plane) =>
        plane == Plane.Sandbox && error.Code is { } code && IsConnectCode(code)
            ? FailureMapping.FromConnectCode(code)
            : FailureMapping.FromError(error, plane);

    #endregion

    #region Private Methods

    /// <summary>The machine-readable code: E2B's <c>error_code</c>, or a Connect code. A numeric code adds nothing.</summary>
    private static async Task<string?> ReadCode(HttpContent content, ILogger logger, CancellationToken cancellationToken)
    {
        var body = await ReadStart(content, cancellationToken);

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            return root.ValueKind != JsonValueKind.Object
                ? null
                : root.TryGetProperty("error_code", out var errorCode) && errorCode.ValueKind == JsonValueKind.String
                    ? errorCode.GetString()
                    : root.TryGetProperty("code", out var found) && found.ValueKind == JsonValueKind.String
                        ? found.GetString()
                        : null;
        }
        catch (JsonException exception)
        {
            logger.LogDebug("The sandbox provider sent an error answer that is not JSON at byte {Position}", exception.BytePositionInLine);
            return null;
        }
    }

    private static async Task<byte[]> ReadStart(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaxErrorBytes];
        var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);

        return buffer[..read];
    }

    private static bool? ReadShouldRetry(HttpResponseMessage response) =>
        response.Headers.TryGetValues(ShouldRetryHeader, out var values)
        && bool.TryParse(values.FirstOrDefault(), out var shouldRetry)
            ? shouldRetry
            : null;

    private static bool IsConnectCode(string code) =>
        code is "canceled" or "unknown" or "invalid_argument" or "deadline_exceeded" or "not_found"
            or "already_exists" or "permission_denied" or "resource_exhausted" or "failed_precondition" or "aborted"
            or "out_of_range" or "unimplemented" or "internal" or "unavailable" or "data_loss" or "unauthenticated";

    #endregion
}
