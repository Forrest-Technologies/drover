namespace Drover.Adapters.E2B;

/// <summary>
/// E2B's REST control plane at <c>https://api.{domain}</c>, written against spec/openapi.yml. It needs only the API key
/// and a sandbox id, so the provider checks the sandbox's metadata before calling anything here.
/// </summary>
internal sealed class ControlPlaneClient(IHttpClientFactory httpClients, E2BOptions options, ILogger logger)
{
    #region Private Fields

    private const string ApiKeyHeader = "X-API-Key";

    private const string IdempotencyHeader = "Idempotency-Key";

    private const string GuestUnresponsive = "sandbox_snapshot_guest_agent_unresponsive";

    private readonly Uri baseAddress = new($"https://api.{options.Domain}/");

    #endregion

    #region Public Methods

    public async Task<SandboxReply> Create(NewSandboxRequest body, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Post, "v2/sandboxes", body);
        request.Headers.Add(IdempotencyHeader, idempotencyKey);

        using var response = await SendExpectingSuccess(request, cancellationToken);

        return await ReadJson<SandboxReply>(response, cancellationToken);
    }

    /// <summary>The sandbox's record, or null when the provider has no sandbox with that id.</summary>
    public async Task<SandboxDetailReply?> Get(string sandboxId, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Get, SandboxPath(sandboxId), null);
        using var response = await Send(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccess(response, cancellationToken);

        return await ReadJson<SandboxDetailReply>(response, cancellationToken);
    }

    /// <summary>Kills the sandbox. One that is already gone counts as killed.</summary>
    public async Task Kill(string sandboxId, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Delete, SandboxPath(sandboxId), null);
        using var response = await Send(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccess(response, cancellationToken);
        }
    }

    public async Task SetTimeout(string sandboxId, int seconds, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Post, $"{SandboxPath(sandboxId)}/timeout", new TimeoutRequest(seconds));
        using var response = await SendExpectingSuccess(request, cancellationToken);
    }

    public async Task Pause(string sandboxId, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Post, $"{SandboxPath(sandboxId)}/pause", null);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await SendExpectingSuccess(request, cancellationToken);
    }

    /// <summary>
    /// Resumes a paused sandbox or extends a running one. A snapshot taken while the sandbox's agent was not answering
    /// can only resume from disk, so that one refusal is answered by asking again without memory.
    /// </summary>
    public async Task<SandboxReply> Connect(string sandboxId, int seconds, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Post, $"v2/{SandboxPath(sandboxId)}/connect", new ConnectSandboxRequest(seconds, null));
        using var response = await Send(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return await ReadJson<SandboxReply>(response, cancellationToken);
        }

        var error = await ProviderErrors.ReadError(response, logger, cancellationToken);

        if (error.Code != GuestUnresponsive)
        {
            throw ProviderErrors.Map(error, Plane.ControlPlane);
        }

        logger.LogWarning("Sandbox {SandboxId} was paused unresponsive and resumes from disk", sandboxId);

        using var fromDisk = Build(HttpMethod.Post, $"v2/{SandboxPath(sandboxId)}/connect", new ConnectSandboxRequest(seconds, false));
        using var resumed = await SendExpectingSuccess(fromDisk, cancellationToken);

        return await ReadJson<SandboxReply>(resumed, cancellationToken);
    }

    public async Task UpdateNetwork(string sandboxId, NetworkConfig network, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Put, $"{SandboxPath(sandboxId)}/network", network);
        using var response = await SendExpectingSuccess(request, cancellationToken);
    }

    #endregion

    #region Private Methods

    private static string SandboxPath(string sandboxId) => $"sandboxes/{Uri.EscapeDataString(sandboxId)}";

    private HttpRequestMessage Build(HttpMethod method, string path, object? body)
    {
        if (string.IsNullOrEmpty(options.ApiKey))
        {
            throw FailureMapping.MissingKey();
        }

        var request = new HttpRequestMessage(method, new Uri(baseAddress, path));
        request.Headers.TryAddWithoutValidation(ApiKeyHeader, options.ApiKey);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Wire.Json);
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendExpectingSuccess(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await Send(request, cancellationToken);

        try
        {
            await EnsureSuccess(response, cancellationToken);
        }
        catch (SandboxProviderException)
        {
            response.Dispose();
            throw;
        }

        return response;
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClients.CreateClient(E2BSandboxProvider.ApiClientName).SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (FailureMapping.TryMapTransport(exception, out var mapped))
        {
            logger.LogWarning("The sandbox provider's control plane failed with {Failure}", mapped.Failure);
            throw mapped;
        }
    }

    private async Task EnsureSuccess(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await ProviderErrors.Read(response, Plane.ControlPlane, logger, cancellationToken);
        }
    }

    private static async Task<T> ReadJson<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Wire.Json, cancellationToken)
                ?? throw FailureMapping.Malformed();
        }
        catch (JsonException)
        {
            throw FailureMapping.Malformed();
        }
    }

    #endregion
}
