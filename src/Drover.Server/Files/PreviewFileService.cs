namespace Drover.Server.Files;

/// <summary>Authorizes one HTML file under its tenant, then serves its current bytes through one consumed grant.</summary>
public sealed class PreviewFileService(
    RunScopes scopes,
    IFileStore store,
    PreviewGrantStore grants,
    ILogger<PreviewFileService> logger,
    PreviewOrigin? origin = null)
{
    #region Private Fields

    private const int MaxPreviewBytes = 2 * 1024 * 1024;

    private const string MissingOrigin =
        "HTML preview is unavailable. The server operator must configure a separate preview origin. Download is still available.";

    #endregion

    #region Public Methods

    public async Task<FilePreviewResponse?> Mint(Guid spaceId, Guid userId, Guid fileId, CancellationToken cancellationToken)
    {
        if (origin is null)
        {
            return new(null, MissingOrigin);
        }

        var file = await Find(spaceId, userId, fileId, cancellationToken);
        if (file is null)
        {
            return null;
        }

        if (file.ContentType != "text/html")
        {
            return new(null, "Only HTML files use the isolated preview. Download is still available.");
        }

        if (file.Size > MaxPreviewBytes)
        {
            return new(null, $"HTML preview is limited to {MaxPreviewBytes:N0} bytes. Download is still available.");
        }

        var token = grants.Add(spaceId, userId, file.Id, file.Sha256);
        if (token is null)
        {
            return new(null, "HTML preview is busy. Download is still available.");
        }

        return new(new Uri(origin.Preview, $"preview/file?grant={Uri.EscapeDataString(token)}").ToString());
    }

    public async Task<string?> Take(string token, CancellationToken cancellationToken)
    {
        if (grants.Take(token) is not { } grant)
        {
            return null;
        }

        var file = await Find(grant.SpaceId, grant.UserId, grant.FileId, cancellationToken);
        if (file is null || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(file.Sha256), Encoding.ASCII.GetBytes(grant.Sha256)))
        {
            return null;
        }

        await using var content = await store.Open(grant.SpaceId, file.StorageKey, cancellationToken);
        if (content is null)
        {
            logger.LogWarning("File {FileId} has a row and nothing in the file store", file.Id);
            return null;
        }

        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var buffer = new char[MaxPreviewBytes + 1];
        var read = await reader.ReadBlockAsync(buffer, cancellationToken);
        if (read > MaxPreviewBytes)
        {
            return null;
        }

        return HtmlPreviewSanitizer.Clean(new string(buffer, 0, read));
    }

    #endregion

    #region Private Methods

    private async Task<StoredFile?> Find(Guid spaceId, Guid userId, Guid fileId, CancellationToken cancellationToken) =>
        await scopes.For(spaceId, userId).Transact(
            services => services.GetRequiredService<FileRepository>().FindFile(fileId, cancellationToken),
            cancellationToken);

    #endregion
}
