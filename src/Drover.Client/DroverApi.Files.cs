namespace Drover.Client;

/// <summary>Files a person attaches to a message: what the bot can take, uploading, removing, and where to fetch one.</summary>
public sealed partial class DroverApi
{
    #region Files

    /// <summary>What the thread's bot can take and the server's file limits, so the composer can refuse before uploading.</summary>
    public Task<AttachmentRules> GetAttachmentRules(Guid spaceId, Guid threadId, CancellationToken cancellationToken = default) =>
        Get<AttachmentRules>($"{Space(spaceId)}/threads/{threadId}/attachment-rules", cancellationToken);

    /// <summary>
    /// Uploads a file for a message not sent yet. <paramref name="progress"/> hears how many bytes have been handed
    /// on so far. A refused file throws <see cref="DroverApiException"/> with the server's sentence and its
    /// <see cref="DroverApiException.Refusal"/> code.
    /// </summary>
    public async Task<FileResponse> UploadFile(
        Guid spaceId,
        Guid threadId,
        string name,
        Stream content,
        long size,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Space(spaceId)}/threads/{threadId}/files?name={Uri.EscapeDataString(name)}")
        {
            Content = new ProgressContent(content, size, progress),
        };

        if (AntiforgeryToken is { Length: > 0 } token)
        {
            request.Headers.Add(AntiforgeryHeader, token);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        return await Read<FileResponse>(response, cancellationToken);
    }

    /// <summary>Removes a file the caller attached and has not sent.</summary>
    public Task RemoveFile(Guid spaceId, Guid fileId, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Delete, $"{Space(spaceId)}/files/{fileId}", cancellationToken);

    /// <summary>Where a file is read from, for a picture's source or a download link. The server checks who asks.</summary>
    public string FileAddress(Guid spaceId, Guid fileId, bool download = false) =>
        new Uri(http.BaseAddress!, $"{Space(spaceId)}/files/{fileId}{(download ? "?download=true" : string.Empty)}").ToString();

    #endregion

    #region Nested Types

    /// <summary>The file's bytes as a request body of known length, telling how far the send has read.</summary>
    private sealed class ProgressContent(Stream content, long size, IProgress<long>? progress) : HttpContent
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
            length = size;
            return true;
        }

        #endregion
    }

    #endregion
}
