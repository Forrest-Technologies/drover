namespace Drover.Client;

/// <summary>
/// The server refused a request. <see cref="Exception.Message"/> is the sentence the server wrote for the person, when it
/// wrote one. <see cref="StatusCode"/> says what kind of refusal: 404 not found or not yours to see, 409 a run is
/// still active in the thread, 400 the request was not valid.
/// </summary>
public sealed class DroverApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    #region Properties

    public HttpStatusCode StatusCode => statusCode;

    /// <summary>Why a file was refused, as one of the <c>FileRefusals</c> codes, when the server named one.</summary>
    public string? Refusal { get; init; }

    #endregion

    #region Public Methods

    public static async Task<DroverApiException> From(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var (title, refusal) = await ReadProblem(response, cancellationToken);
        return new DroverApiException(response.StatusCode, title ?? $"The server refused the request ({(int)response.StatusCode}).")
        {
            Refusal = refusal,
        };
    }

    #endregion

    #region Private Methods

    private static async Task<(string? Title, string? Refusal)> ReadProblem(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/problem+json" or "application/json"))
        {
            return (null, null);
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text));

        // TryParseValue reports malformed JSON as false, so a body that is not a problem document is simply no title.
        if (!JsonDocument.TryParseValue(ref reader, out var problem))
        {
            return (null, null);
        }

        using (problem)
        {
            return (ReadString(problem.RootElement, "title"), ReadString(problem.RootElement, "refusal"));
        }
    }

    private static string? ReadString(JsonElement problem, string name) =>
        problem.ValueKind is JsonValueKind.Object
        && problem.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    #endregion
}
