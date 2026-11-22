namespace Drover.Server.Tests;

public sealed class NativePictureCacheTests
{
    #region Public Methods

    [Fact]
    public async Task A_slow_store_read_leaves_room_for_another_space()
    {
        using var cache = Create(NullLogger<NativePictureCache>.Instance);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var slow = new GatedStream(release.Task);
        var first = Read(cache, Download(Guid.NewGuid(), slow), Guid.NewGuid());
        try
        {
            await slow.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await using var fast = new MemoryStream([0]);
            var other = await Read(cache, Download(Guid.NewGuid(), fast), Guid.NewGuid())
                .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.Equal(NativePreviewRefusal.Malformed, other.Refusal);
        }
        finally
        {
            release.SetResult();
            await first;
        }
    }

    [Fact]
    public async Task A_finished_copy_survives_cancellation_at_decode()
    {
        using var cancelled = new CancellationTokenSource();
        var logger = new DecodeLog(cancelled);
        using var cache = Create(logger);
        var png = Convert.FromBase64String("""
            iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAFUlEQVR4nGNg2HCBNDSq
            YVTD8NUAAFV1gBC4fs/iAAAAAElFTkSuQmCC
            """);
        await using var bytes = new MemoryStream(png);
        var file = Download(Guid.NewGuid(), bytes);
        file.File.Size = png.Length;
        var first = await cache.Read(file, Guid.NewGuid(), cancelled.Token);
        Assert.True(cancelled.IsCancellationRequested);
        Assert.NotNull(first.Image);
        var second = await Read(cache, file, Guid.NewGuid());
        Assert.Same(first, second);
        await using var malformed = new MemoryStream([0]);
        await Read(cache, Download(Guid.NewGuid(), malformed), Guid.NewGuid());
        Assert.Contains("Malformed", logger.Messages);
        Assert.DoesNotContain(file.File.Name, logger.Messages, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_member_at_the_rate_limit_can_read_a_hit_and_another_member_can_start_a_copy()
    {
        var time = new AdjustableTimeProvider();
        using var cache = new NativePictureCache(NullLogger<NativePictureCache>.Instance, time);
        var user = Guid.NewGuid();
        var space = Guid.NewGuid();
        await using var bytes = new MemoryStream([0]);
        var firstFile = Download(space, bytes);
        var first = await Read(cache, firstFile, user);
        for (var start = 1; start < NativeFilePreview.MaxPicturePreviewsPerUserPerMinute; start++)
        {
            bytes.Position = 0;
            var result = await Read(cache, Download(space, bytes), user);
            Assert.Equal(NativePreviewRefusal.Malformed, result.Refusal);
        }

        var busy = await Read(cache, Download(space, bytes), user);
        Assert.Equal(NativePreviewRefusal.TooComplex, busy.Refusal);
        Assert.Same(first, await Read(cache, firstFile, user));
        bytes.Position = 0;
        var other = await Read(cache, Download(space, bytes), Guid.NewGuid());
        Assert.Equal(NativePreviewRefusal.Malformed, other.Refusal);
        time.Advance(TimeSpan.FromMinutes(1));
        bytes.Position = 0;
        var available = await Read(cache, Download(space, bytes), user);
        Assert.Equal(NativePreviewRefusal.Malformed, available.Refusal);
    }

    [Fact]
    public async Task Six_cold_pictures_wait_for_member_capacity_and_all_get_read()
    {
        using var cache = Create(NullLogger<NativePictureCache>.Instance);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var user = Guid.NewGuid();
        var space = Guid.NewGuid();
        var streams = Enumerable.Range(0, 6).Select(_ => new GatedStream(release.Task)).ToArray();
        var requests = streams.Select(stream => Read(cache, Download(space, stream), user)).ToArray();
        try
        {
            await streams[1].Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(2, streams.Count(stream => stream.Entered.Task.IsCompleted));
            Assert.All(requests, request => Assert.False(request.IsCompleted));
        }
        finally
        {
            release.SetResult();
            await Task.WhenAll(requests);
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }

        Assert.All(streams, stream => Assert.True(stream.Entered.Task.IsCompleted));
    }

    [Fact]
    public async Task Cancelling_a_queued_request_releases_its_place_without_reading_it()
    {
        using var cache = Create(NullLogger<NativePictureCache>.Instance);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var space = Guid.NewGuid();
        var member = Guid.NewGuid();
        await using var first = new GatedStream(release.Task);
        await using var second = new GatedStream(release.Task);
        await using var abandoned = new GatedStream(release.Task);
        var readers = Task.WhenAll(
            Read(cache, Download(space, first), member), Read(cache, Download(space, second), member));
        try
        {
            var queued = cache.Read(Download(space, abandoned), member, cancelled.Token);
            await cancelled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
            Assert.False(abandoned.Entered.Task.IsCompleted);
        }
        finally
        {
            release.SetResult();
            await readers;
        }
    }

    [Theory]
    [InlineData(true, NativeFilePreview.MaxPendingPicturesPerUser)]
    [InlineData(false, NativeFilePreview.MaxPendingPicturesPerSpace)]
    public async Task An_admission_limit_leaves_capacity_for_other_members(bool sameUser, int limit)
    {
        using var cache = Create(NullLogger<NativePictureCache>.Instance);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var space = Guid.NewGuid();
        var user = Guid.NewGuid();
        var streams = Enumerable.Range(0, limit).Select(_ => new GatedStream(release.Task)).ToArray();
        var pending = streams.Select(stream => Read(cache, Download(space, stream), sameUser ? user : Guid.NewGuid()))
            .ToArray();
        try
        {
            await streams[0].Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await using var extra = new MemoryStream([0]);
            var busy = await Read(cache, Download(space, extra), sameUser ? user : Guid.NewGuid())
                .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.Equal(NativePreviewRefusal.TooComplex, busy.Refusal);

            if (sameUser)
            {
                var other = await Read(cache, Download(space, extra), Guid.NewGuid())
                    .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
                Assert.Equal(NativePreviewRefusal.Malformed, other.Refusal);
            }
        }
        finally
        {
            release.SetResult();
            await Task.WhenAll(pending);
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    #endregion

    #region Private Methods

    private static NativePictureCache Create(ILogger<NativePictureCache> logger) => new(logger, TimeProvider.System);

    private static Task<NativeFilePreview> Read(NativePictureCache cache, FileDownload download, Guid user) =>
        cache.Read(download, user, TestContext.Current.CancellationToken);

    private static FileDownload Download(Guid space, Stream content) =>
        new(new StoredFile
        {
            SpaceId = space,
            Name = "private-file-name",
            ContentType = "image/png",
            Sha256 = "a",
            StorageKey = "key",
            Size = 1
        }, content);

    #endregion

    #region Nested Types

    private sealed class DecodeLog(CancellationTokenSource cancelled) : ILogger<NativePictureCache>
    {
        #region Private Fields

        private readonly List<string> messages = [];

        #endregion

        #region Properties

        public string Messages => string.Join('\n', messages);

        #endregion

        #region Interface Implementations

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel level) => true;

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> format)
        {
            messages.Add(format(state, exception));
            if (level == LogLevel.Debug)
            {
                cancelled.Cancel();
            }
        }

        #endregion
    }

    private sealed class GatedStream(Task release) : MemoryStream(new byte[1])
    {
        #region Properties

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        #endregion

        #region Public Methods

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await release.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }

        #endregion
    }

    #endregion
}
