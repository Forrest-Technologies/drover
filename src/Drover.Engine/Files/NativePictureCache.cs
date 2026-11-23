namespace Drover.Engine.Files;

/// <summary>Caches bounded picture copies after the route has authorized the original.</summary>
public sealed class NativePictureCache(ILogger<NativePictureCache> logger, TimeProvider time) : IDisposable
{
    #region Private Fields

    private readonly MemoryCache copies = new(new MemoryCacheOptions { SizeLimit = 16 * 1024 * 1024 });
    private readonly MemoryCache starts = new(new MemoryCacheOptions());
    private readonly Lock admission = new();
    private readonly List<PictureRequest> requests = [];
    private readonly SemaphoreSlim buffers = new(3, 3);
    private readonly SemaphoreSlim decoding = new(1, 1);

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

        if (Admit(userId, file.SpaceId) is not { } request)
        {
            logger.LogDebug("Picture preview admission refused for {FileId}", file.Id);
            return new(Refusal: NativePreviewRefusal.TooComplex);
        }

        try
        {
            if (!await request.WaitForTurn(time, logger, cancellationToken))
            {
                return new(Refusal: NativePreviewRefusal.TooComplex);
            }

            return await CreateCopy(found, key, cancellationToken);
        }
        finally
        {
            lock (admission)
            {
                requests.Remove(request);
                foreach (var waiting in requests)
                {
                    StartIfAvailable(waiting);
                }
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

    private PictureRequest? Admit(Guid userId, Guid spaceId)
    {
        lock (admission)
        {
            if (requests.Count >= NativeFilePreview.MaxPendingPictures
                || requests.Count(request => request.UserId == userId) >= NativeFilePreview.MaxPendingPicturesPerUser
                || requests.Count(request => request.SpaceId == spaceId) >= NativeFilePreview.MaxPendingPicturesPerSpace
                || !TryChargeStart(userId))
            {
                return null;
            }

            var request = new PictureRequest(userId, spaceId);
            requests.Add(request);
            StartIfAvailable(request);
            return request;
        }
    }

    private bool TryChargeStart(Guid userId)
    {
        var now = time.GetUtcNow();
        var window = TimeSpan.FromMinutes(1);
        var recent = starts.Get<Queue<DateTimeOffset>>(userId) ?? new();
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
        return true;
    }

    // Under the admission lock, consider older requests first and let other members use free capacity.
    private void StartIfAvailable(PictureRequest request)
    {
        if (!request.Started
            && requests.Count(other => other.Started && other.UserId == request.UserId)
                < NativeFilePreview.MaxPicturesPerUser
            && requests.Count(other => other.Started && other.SpaceId == request.SpaceId)
                < NativeFilePreview.MaxPicturesPerSpace)
        {
            request.Start();
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
            var preview = await NativeImageDecodeGuard.Run(
                () => NativeImagePreview.Resize(bytes),
                logger,
                time);
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

    #region Nested Types

    private sealed class PictureRequest(Guid userId, Guid spaceId)
    {
        #region Private Fields

        private readonly TaskCompletionSource turn = new(TaskCreationOptions.RunContinuationsAsynchronously);

        #endregion

        #region Properties

        public Guid UserId { get; } = userId;

        public Guid SpaceId { get; } = spaceId;

        public bool Started => turn.Task.IsCompleted;

        #endregion

        #region Public Methods

        public void Start() => turn.SetResult();

        public async Task<bool> WaitForTurn(TimeProvider time, ILogger logger, CancellationToken cancellationToken)
        {
            try
            {
                await turn.Task.WaitAsync(TimeSpan.FromSeconds(8), time, cancellationToken);
                return true;
            }
            catch (TimeoutException)
            {
                logger.LogDebug("Picture preview admission timed out for space {SpaceId}", SpaceId);
                return false;
            }
        }

        #endregion
    }

    #endregion
}
