namespace Drover.Client;

/// <summary>Speech: what a space can use, its choices, writing down a recording, reading aloud and a live call.</summary>
public sealed partial class DroverApi
{
    #region Voice

    /// <summary>The models that can listen, speak and hold a live call in the space, the one each uses now, and the server's limits.</summary>
    public Task<VoiceOptionsResponse> GetVoiceOptions(Guid spaceId, CancellationToken cancellationToken = default) =>
        Get<VoiceOptionsResponse>($"{Space(spaceId)}/voice/options", cancellationToken);

    /// <summary>Saves which model fills each slot. A slot left null is chosen by the server. Admins only.</summary>
    public Task<VoiceOptionsResponse> SaveVoiceSettings(Guid spaceId, VoiceSettingsRequest settings, CancellationToken cancellationToken = default) =>
        Send<VoiceOptionsResponse>(HttpMethod.Put, $"{Space(spaceId)}/voice/settings", settings, cancellationToken);

    /// <summary>The voices a speech model offers. A null <paramref name="connectionId"/> asks the server's own speech service.</summary>
    public Task<List<VoiceOptionResponse>> ListVoices(Guid spaceId, Guid? connectionId, string model, CancellationToken cancellationToken = default) =>
        Get<List<VoiceOptionResponse>>(
            $"{Space(spaceId)}/voice/voices?model={Uri.EscapeDataString(model)}{(connectionId is { } id ? $"&connectionId={id}" : string.Empty)}",
            cancellationToken);

    /// <summary>Says what a model of a compatible connection does with speech. All three false clears the mark. Admins only.</summary>
    public Task<ModelCatalogResponse> SetModelVoice(
        Guid spaceId,
        Guid connectionId,
        SetModelVoiceRequest request,
        CancellationToken cancellationToken = default) =>
        Send<ModelCatalogResponse>(HttpMethod.Put, $"{Space(spaceId)}/model-connections/{connectionId}/models/voice", request, cancellationToken);

    /// <summary>
    /// Writes down a recording. <paramref name="mediaType"/> names its audio format: audio/webm, audio/wav,
    /// audio/mp4, audio/mpeg or audio/ogg. The server stores nothing of it.
    /// </summary>
    public async Task<string> Transcribe(Guid spaceId, Stream recording, string mediaType, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Space(spaceId)}/voice/transcribe");
        request.Content = new StreamContent(recording);
        request.Content.Headers.TryAddWithoutValidation("Content-Type", mediaType);

        if (AntiforgeryToken is { Length: > 0 } token)
        {
            request.Headers.Add(AntiforgeryHeader, token);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        return (await Read<TranscriptResponse>(response, cancellationToken)).Text;
    }

    /// <summary>
    /// Reads a message, or a sample text, aloud. The answer is returned as soon as the audio begins, with its body
    /// still arriving, so a player can start at once and a long answer is not cut off by the client's wait for a
    /// whole response. Its content type names the audio format. The caller disposes it.
    /// </summary>
    public async Task<HttpResponseMessage> Speak(Guid spaceId, SpeakRequest speak, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Space(spaceId)}/voice/speak");
        request.Content = JsonContent.Create(speak, options: json);

        if (AntiforgeryToken is { Length: > 0 } token)
        {
            request.Headers.Add(AntiforgeryHeader, token);
        }

        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        try
        {
            await response.ThrowIfRefused(cancellationToken);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens a live call with a conversation's bot. The server is first asked plainly whether it would take the
    /// call, because a refused WebSocket handshake cannot carry the reason back: a refusal throws
    /// <see cref="DroverApiException"/> with the server's sentence. <paramref name="connect"/> opens the socket; a
    /// host whose sign-in is not a browser cookie passes one that adds its own credentials. Left null, a plain
    /// socket is opened, which is what a browser needs.
    /// </summary>
    public async Task<VoiceCall> OpenCall(
        Guid spaceId,
        Guid threadId,
        Func<Uri, CancellationToken, Task<WebSocket>>? connect = null,
        CancellationToken cancellationToken = default)
    {
        var path = $"{Space(spaceId)}/threads/{threadId}/call";

        using (var asked = await http.GetAsync(path, cancellationToken))
        {
            if (asked.StatusCode is not HttpStatusCode.UpgradeRequired)
            {
                throw await DroverApiException.From(asked, cancellationToken);
            }
        }

        var address = new UriBuilder(new Uri(http.BaseAddress!, path));
        address.Scheme = address.Scheme == Uri.UriSchemeHttp ? "ws" : "wss";

        try
        {
            return new VoiceCall(await (connect ?? ConnectPlainly)(address.Uri, cancellationToken));
        }
        catch (Exception refused) when (refused is WebSocketException or InvalidOperationException or HttpRequestException)
        {
            throw new DroverApiException(HttpStatusCode.BadGateway, "The call could not be connected. Try again.") { Refusal = refused.GetType().Name };
        }
    }

    private static async Task<WebSocket> ConnectPlainly(Uri address, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();

        try
        {
            await socket.ConnectAsync(address, cancellationToken);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    #endregion
}
