namespace Drover.Client;

/// <summary>
/// Files in a conversation: what the bot can take, uploading, removing, where to fetch one, its text, and the files a
/// person and a bot exchanged.
/// </summary>
public sealed partial class DroverApi
{
    #region Files

    /// <summary>Opens a conversation file as a stream; the caller disposes the response.</summary>
    public async Task<HttpResponseMessage> OpenConversationFile(string address, CancellationToken cancellationToken = default)
    {
        if (!TryConversationFile(address, out var spaceId, out var fileId))
        {
            throw new ArgumentException("A conversation file must be on the Drover server.", nameof(address));
        }

        var response = await http.GetAsync(FileAddress(spaceId, fileId, download: true),
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

    /// <summary>Issues a short-lived, single-use address for a bot HTML file on the isolated preview origin.</summary>
    public Task<FilePreviewResponse> PreviewConversationFile(string address, CancellationToken cancellationToken = default)
    {
        if (!TryConversationFile(address, out var spaceId, out var fileId))
        {
            throw new ArgumentException("A conversation file must be on the Drover server.", nameof(address));
        }

        return Post<FilePreviewResponse>($"{Space(spaceId)}/files/{fileId}/preview", null, cancellationToken);
    }

    /// <summary>Invalidates an unused HTML preview grant. An already consumed grant makes this a no-op.</summary>
    public Task RevokeConversationFilePreview(
        string address,
        string grant,
        CancellationToken cancellationToken = default)
    {
        if (!TryConversationFile(address, out var spaceId, out var fileId))
        {
            throw new ArgumentException("A conversation file must be on the Drover server.", nameof(address));
        }

        return SendForNothing(
            HttpMethod.Post,
            $"{Space(spaceId)}/files/{fileId}/preview/revoke",
            new FilePreviewRevokeRequest(grant),
            cancellationToken);
    }

    /// <summary>What the thread's bot can take and the server's file limits, so the composer can refuse before uploading.</summary>
    public Task<AttachmentRules> GetAttachmentRules(Guid spaceId, Guid threadId, CancellationToken cancellationToken = default) =>
        Get<AttachmentRules>($"{Space(spaceId)}/threads/{threadId}/attachment-rules", cancellationToken);

    /// <summary>
    /// Uploads a file for a message not sent yet. <paramref name="progress"/> hears how many bytes have been handed
    /// on so far. A refused file throws <see cref="DroverApiException"/> with the server's sentence and its
    /// <see cref="DroverApiException.Refusal"/> code.
    /// </summary>
    public Task<FileResponse> UploadFile(
        Guid spaceId,
        Guid threadId,
        string name,
        Stream content,
        long size,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default) =>
        SendFile<FileResponse>(
            $"{Space(spaceId)}/threads/{threadId}/files?name={Uri.EscapeDataString(name)}",
            new ProgressContent(content, size, progress),
            cancellationToken);

    /// <summary>Measures and uploads a file whose provider may not know its exact byte length.</summary>
    public Task<FileResponse> UploadFileWithUnverifiedSize(
        Guid spaceId,
        Guid threadId,
        string name,
        Stream content,
        long maxBytes,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default) =>
        BufferAndSendFile<FileResponse>(
            $"{Space(spaceId)}/threads/{threadId}/files?name={Uri.EscapeDataString(name)}",
            content,
            maxBytes,
            progress,
            cancellationToken);

    /// <summary>Removes a file the caller attached and has not sent.</summary>
    public Task RemoveFile(Guid spaceId, Guid fileId, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Delete, $"{Space(spaceId)}/files/{fileId}", cancellationToken);

    /// <summary>The files shared in a project, newest first.</summary>
    public Task<IReadOnlyList<ProjectFileResponse>> ListProjectFiles(Guid spaceId, Guid projectId, CancellationToken cancellationToken = default) =>
        Get<IReadOnlyList<ProjectFileResponse>>($"{Space(spaceId)}/projects/{projectId}/files", cancellationToken);

    /// <summary>Uploads a shared file to a project.</summary>
    public Task<ProjectFileResponse> UploadProjectFile(
        Guid spaceId,
        Guid projectId,
        string name,
        Stream content,
        long size,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default) =>
        SendFile<ProjectFileResponse>(
            $"{Space(spaceId)}/projects/{projectId}/files?name={Uri.EscapeDataString(name)}",
            new ProgressContent(content, size, progress),
            cancellationToken);

    /// <summary>Measures and uploads a shared project file whose provider may not know its exact byte length.</summary>
    public Task<ProjectFileResponse> UploadProjectFileWithUnverifiedSize(
        Guid spaceId,
        Guid projectId,
        string name,
        Stream content,
        long maxBytes,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default) =>
        BufferAndSendFile<ProjectFileResponse>(
            $"{Space(spaceId)}/projects/{projectId}/files?name={Uri.EscapeDataString(name)}",
            content,
            maxBytes,
            progress,
            cancellationToken);

    /// <summary>Removes a shared file from a project.</summary>
    public Task RemoveProjectFile(Guid spaceId, Guid projectId, Guid fileId, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Delete, $"{Space(spaceId)}/projects/{projectId}/files/{fileId}", cancellationToken);

    /// <summary>Where a shared project file is read from, for a download link or preview. The server checks who asks.</summary>
    public string ProjectFileAddress(Guid spaceId, Guid projectId, Guid fileId, bool download = false) =>
        new Uri(http.BaseAddress!, $"{Space(spaceId)}/projects/{projectId}/files/{fileId}{(download ? "?download=true" : string.Empty)}").ToString();

    /// <summary>Where a file is read from, for a picture's source or a download link. The server checks who asks.</summary>
    public string FileAddress(Guid spaceId, Guid fileId, bool download = false) =>
        new Uri(http.BaseAddress!, $"{Space(spaceId)}/files/{fileId}{(download ? "?download=true" : string.Empty)}").ToString();

    /// <summary>The files the caller and the bot exchanged in the caller's conversations with it, newest first.</summary>
    public Task<IReadOnlyList<ExchangedFileResponse>> ListExchangedFiles(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<IReadOnlyList<ExchangedFileResponse>>($"{Space(spaceId)}/bots/{botId}/files", cancellationToken);

    /// <summary>
    /// A text file's content, up to <paramref name="maxCharacters"/>, and whether there is more past that. The server
    /// serves text as plain text, so nothing in it is run.
    /// </summary>
    public async Task<FileText> ReadFileText(Guid spaceId, Guid fileId, int maxCharacters, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"{Space(spaceId)}/files/{fileId}", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await response.ThrowIfRefused(cancellationToken);
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(content, Encoding.UTF8);
        // One character past the bound says whether there is more. A browser's response stream reads only
        // asynchronously, so the reader cannot peek.
        var buffer = new char[maxCharacters + 1];
        var read = await reader.ReadBlockAsync(buffer, cancellationToken);
        return new FileText(new string(buffer, 0, Math.Min(read, maxCharacters)), read > maxCharacters);
    }

    #endregion

    #region Private Methods

    private async Task<T> BufferAndSendFile<T>(
        string path,
        Stream content,
        long maxBytes,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        using var body = new ProgressContent(content, null, progress);
        try
        {
            // Android's HTTP handler chooses fixed-length framing before it buffers the content. Buffer first so
            // Content-Length comes from the bytes themselves, even when the picker returned zero or a stale size.
            await body.LoadIntoBufferAsync(maxBytes, cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.HttpRequestError is HttpRequestError.ConfigurationLimitExceeded)
        {
            throw new DroverApiException(HttpStatusCode.RequestEntityTooLarge, "The file is larger than the server takes.")
            {
                Refusal = FileRefusals.TooLarge,
            };
        }

        return await SendFile<T>(path, body, cancellationToken);
    }

    private async Task<T> SendFile<T>(string path, HttpContent body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = body,
        };

        if (AntiforgeryToken is { Length: > 0 } token)
        {
            request.Headers.Add(AntiforgeryHeader, token);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        return await Read<T>(response, cancellationToken);
    }

    private bool TryConversationFile(string address, out Guid spaceId, out Guid fileId)
    {
        spaceId = default;
        fileId = default;
        return http.BaseAddress is { } origin
            && Uri.TryCreate(origin, address, out var file)
            && origin.IsBaseOf(file)
            && file.AbsolutePath.Split('/') is ["", "api", "spaces", var space, "files", var id]
            && Guid.TryParse(space, out spaceId)
            && Guid.TryParse(id, out fileId);
    }

    #endregion

    #region Nested Types

    /// <summary>The file's bytes as a request body, telling how far the send has read.</summary>
    private sealed class ProgressContent(Stream content, long? size, IProgress<long>? progress) : HttpContent
    {
        #region Private Fields

        private const int ChunkSize = 64 * 1024;

        #endregion

        #region Overrides

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            var buffer = new byte[ChunkSize];
            long sent = 0;
            int read;

            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                sent += read;
                progress?.Report(sent);
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override bool TryComputeLength(out long length)
        {
            length = size.GetValueOrDefault();
            return size.HasValue;
        }

        #endregion
    }

    #endregion
}


/// <summary>A text file's content as far as it was read, and whether it goes on past that.</summary>
public sealed record FileText(string Text, bool Cut);
