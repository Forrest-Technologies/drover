namespace Drover.Engine.Files;

/// <summary>Caches bounded picture copies after the route has authorized the original.</summary>
public sealed class NativePictureCache : IDisposable
{
    #region Private Fields

    private readonly MemoryCache copies = new(new MemoryCacheOptions { SizeLimit = 16 * 1024 * 1024 });

    // One decode at a time bounds the combined working memory and avoids duplicating a cache miss.
    private readonly SemaphoreSlim decoding = new(1, 1);

    private int pending;

    #endregion

    #region Public Methods

    public async Task<NativeFilePreview> Read(FileDownload found, CancellationToken cancellationToken)
    {
        var file = found.File;
        if (file.Size is <= 0 or > DeploymentPolicy.SelfHostedMaxFileBytes)
        {
            return new(Refusal: NativePreviewRefusal.TooLarge);
        }

        var key = (file.SpaceId, file.Id, file.Sha256);
        if (copies.Get<NativeFilePreview>(key) is { } existing)
        {
            return existing;
        }

        try
        {
            if (Interlocked.Increment(ref pending) > 32)
            {
                return new(Refusal: NativePreviewRefusal.TooComplex);
            }

            return await CreateCopy(found, key, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref pending);
        }
    }

    #endregion

    #region Interface Implementations

    public void Dispose()
    {
        copies.Dispose();
        decoding.Dispose();
    }

    #endregion

    #region Private Methods

    private async Task<NativeFilePreview> CreateCopy(
        FileDownload found,
        (Guid SpaceId, Guid FileId, string Hash) key,
        CancellationToken cancellationToken)
    {
        await decoding.WaitAsync(cancellationToken);
        try
        {
            if (copies.Get<NativeFilePreview>(key) is { } ready)
            {
                return ready;
            }

            var bytes = new byte[(int)found.File.Size];
            var read = await found.Content.ReadAtLeastAsync(bytes, bytes.Length, false, cancellationToken);
            var extra = new byte[1];
            if (read != bytes.Length || await found.Content.ReadAsync(extra, cancellationToken) != 0)
            {
                return new(Refusal: NativePreviewRefusal.Malformed);
            }

            var preview = NativeImagePreview.Resize(bytes);
            cancellationToken.ThrowIfCancellationRequested();
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                Size = 256L + (preview.Image?.Length ?? 0),
            };
            return copies.Set(key, preview, options);
        }
        finally
        {
            decoding.Release();
        }
    }

    #endregion
}
