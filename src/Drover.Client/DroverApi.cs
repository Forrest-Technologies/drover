namespace Drover.Client;

/// <summary>
/// The typed client for Drover's API. Every screen goes through it, in the browser and in the app shell alike, so
/// there is one data path. The calls live in partial files by area; this file holds what they share.
/// </summary>
public sealed partial class DroverApi(HttpClient http)
{
    #region Private Fields

    private const string AntiforgeryHeader = "X-XSRF-TOKEN";

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    #endregion

    #region Properties

    /// <summary>
    /// The antiforgery request token, read by the host from the <c>XSRF-TOKEN</c> cookie. When set, every
    /// state-changing request carries it; the server refuses those without it.
    /// </summary>
    public string? AntiforgeryToken { get; set; }

    #endregion

    #region Private Methods

    private static string Space(Guid spaceId) => $"api/spaces/{spaceId}";

    private async Task<T> Get<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        return await Read<T>(response, cancellationToken);
    }

    private Task<T> Post<T>(string path, object? body, CancellationToken cancellationToken) =>
        Send<T>(HttpMethod.Post, path, body, cancellationToken);

    private async Task<T> Send<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await Send(method, path, body, cancellationToken);
        return await Read<T>(response, cancellationToken);
    }

    /// <summary>A state-changing request whose answer has no body. A refusal throws like any other.</summary>
    private Task SendForNothing(HttpMethod method, string path, CancellationToken cancellationToken) =>
        SendForNothing(method, path, null, cancellationToken);

    private async Task SendForNothing(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await Send(method, path, body, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await DroverApiException.From(response, cancellationToken);
        }
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: json);
        }

        if (AntiforgeryToken is { Length: > 0 } token)
        {
            request.Headers.Add(AntiforgeryHeader, token);
        }

        return await http.SendAsync(request, cancellationToken);
    }

    private static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await DroverApiException.From(response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<T>(json, cancellationToken)
            ?? throw new DroverApiException(response.StatusCode, "The server answered with nothing.");
    }

    #endregion
}
