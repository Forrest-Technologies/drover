namespace Drover.Engine.Files;

/// <summary>Caches bounded picture copies after the route has authorized the original.</summary>
public sealed class NativePictureCache(ILogger<NativePictureCache> logger, TimeProvider time) : IDisposable
{
    #region Private Fields

    private readonly MemoryCache copies = new(new MemoryCacheOptions { SizeLimit = 16 * 1024 * 1024 });
    private readonly MemoryCache starts = new(new MemoryCacheOptions());
    private readonly Lock admission = new();
    private readonly Dictionary<Guid, int> users = [];
    private readonly Dictionary<Guid, int> spaces = [];
    private readonly SemaphoreSlim buffers = new(3, 3);
    private readonly SemaphoreSlim decoding = new(1, 1);
    private int pending;

    #endregion

    #region Public Methods

    public async Task<NativeFilePreview> Read(FileDownload found, Guid userId, CancellationToken cancellationToken)
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

        if (!TryAdmit(userId, file.SpaceId))
        {
            logger.LogDebug("Picture preview admission refused for {FileId}", file.Id);
            return new(Refusal: NativePreviewRefusal.TooComplex);
        }

        try
        {
            return await CreateCopy(found, key, cancellationToken);
        }
        finally
        {
            lock (admission)
            {
                Release(users, userId);
                Release(spaces, file.SpaceId);
                pending--;
            }
        }
    }

    #endregion

    #region Interface Implementations

    public void Dispose()
    {
        copies.Dispose();
        starts.Dispose();
        buffers.Dispose();
        decoding.Dispose();
    }

    #endregion

    #region Private Methods

    private bool TryAdmit(Guid userId, Guid spaceId)
    {
        lock (admission)
        {
            var userCount = users.GetValueOrDefault(userId);
            var spaceCount = spaces.GetValueOrDefault(spaceId);
            if (pending >= NativeFilePreview.MaxPendingPictures || userCount >= NativeFilePreview.MaxPicturesPerUser
                || spaceCount >= NativeFilePreview.MaxPicturesPerSpace)
            {
                return false;
            }

            var now = time.GetUtcNow();
            var window = TimeSpan.FromMinutes(1);
            var recent = starts.Get<Queue<DateTimeOffset>>(userId) ?? new Queue<DateTimeOffset>();
            while (recent.TryPeek(out var oldest) && now - oldest >= window)
            {
                recent.Dequeue();
            }

            if (recent.Count >= NativeFilePreview.MaxPicturePreviewsPerUserPerMinute)
            {
                return false;
            }

            recent.Enqueue(now);
            starts.Set(userId, recent, window);
            users[userId] = userCount + 1;
            spaces[spaceId] = spaceCount + 1;
            pending++;
            return true;
        }
    }

    private static void Release(Dictionary<Guid, int> counts, Guid key)
    {
        if (--counts[key] == 0)
        {
            counts.Remove(key);
        }
    }

    private async Task<NativeFilePreview> CreateCopy(
        FileDownload found,
        (Guid SpaceId, Guid FileId, string Hash) key,
        CancellationToken cancellationToken)
    {
        // Hold the buffer permit until decoding ends so queued reads cannot retain unbounded originals.
        await buffers.WaitAsync(cancellationToken);
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
                logger.LogWarning("Picture preview length mismatch for {FileId}", key.FileId);
                return new(Refusal: NativePreviewRefusal.Malformed);
            }

            return copies.Get<NativeFilePreview>(key) ?? await Decode(key, bytes, cancellationToken);
        }
        finally
        {
            buffers.Release();
        }
    }

    private async Task<NativeFilePreview> Decode(
        (Guid SpaceId, Guid FileId, string Hash) key,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        await decoding.WaitAsync(cancellationToken);
        try
        {
            if (copies.Get<NativeFilePreview>(key) is { } ready)
            {
                return ready;
            }

            logger.LogDebug("Decoding picture preview for {FileId}", key.FileId);
            var preview = NativeImagePreview.Resize(bytes);
            if (preview.Refusal is { } refusal)
            {
                logger.LogDebug("Picture preview for {FileId} refused with {Refusal}", key.FileId, refusal);
            }

            if (preview.Refusal == NativePreviewRefusal.TooComplex)
            {
                return preview;
            }

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
