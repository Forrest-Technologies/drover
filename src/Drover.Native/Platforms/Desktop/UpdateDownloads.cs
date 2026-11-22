namespace Drover.Native.Platform;

/// <summary>
/// Reads update metadata through a byte limit and a deadline. Nothing an update endpoint sends is trusted until its
/// signature is checked, so every read stops at a fixed size and a fixed time before that check can run.
/// </summary>
internal static class UpdateReads
{
    #region Public Methods

    /// <summary>The most a signed feed or its detached signature may be, in bytes.</summary>
    public const long FeedLimit = 1024 * 1024;

    /// <summary>The most the preview release list may be, in bytes.</summary>
    public const long ReleaseListLimit = 8 * 1024 * 1024;

    public const string TooLargeProblem = "The release information is larger than Drover accepts.";

    public const string TooSlowProblem = "The release information took too long to arrive.";

    /// <summary>The longest a feed, signature, or release list read may take, from request to last byte.</summary>
    public static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The body at <paramref name="address"/>, at most <paramref name="limit"/> bytes, within
    /// <paramref name="deadline"/>. A declared length over the limit is refused before the body is read, and a body
    /// that runs past the limit is refused at the first chunk that crosses it.
    /// </summary>
    public static async Task<byte[]> Read(HttpClient client, string address, long limit, TimeSpan deadline)
    {
        using var timeout = new CancellationTokenSource(deadline);

        try
        {
            using var response = await client.GetAsync(
                address,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > limit)
            {
                throw new InvalidDataException(TooLargeProblem);
            }

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            return await ReadAtMost(body, limit, timeout.Token) ?? throw new InvalidDataException(TooLargeProblem);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(TooSlowProblem, exception);
        }
    }

    #endregion

    #region Private Methods

    private static async Task<byte[]?> ReadAtMost(Stream body, long limit, CancellationToken cancellation)
    {
        using var kept = new MemoryStream();
        var chunk = new byte[81920];

        while (true)
        {
            var count = await body.ReadAsync(chunk, cancellation);

            if (count == 0)
            {
                return kept.ToArray();
            }

            if (kept.Length + count > limit)
            {
                return null;
            }

            await kept.WriteAsync(chunk.AsMemory(0, count), cancellation);
        }
    }

    #endregion
}


/// <summary>Downloads the signed feed and its detached signature through a bounded read.</summary>
internal sealed class BoundedFeedDownloader(HttpClient client, long limit, TimeSpan deadline)
    : NetSparkleUpdater.Interfaces.IAppCastDataDownloader
{
    #region Properties

    public long Limit => limit;

    #endregion

    #region Interface Implementations

    /// <summary>The update library calls only the asynchronous read, so a blocking read is never offered.</summary>
    public string DownloadAndGetAppCastData(string url) =>
        throw new NotSupportedException("Drover reads the update feed only asynchronously.");

    public async Task<string> DownloadAndGetAppCastDataAsync(string url)
    {
        var body = await UpdateReads.Read(client, url, limit, deadline);

        // Decoded as the library's own downloader decodes it, so the signature is checked over the same text.
        using var reader = new StreamReader(new MemoryStream(body), GetAppCastEncoding());
        return await reader.ReadToEndAsync();
    }

    public Encoding GetAppCastEncoding() => Encoding.UTF8;

    #endregion
}


/// <summary>
/// Downloads an update package for the update library, never writing more than <paramref name="limit"/> bytes. A
/// declared length over the limit is refused before the file is created. A body that runs past the limit, a failure,
/// or a cancellation stops the download and removes the partial file before the library is told, so the library
/// never checks the signature of a file over the limit. Every download ends with exactly one completion, and a
/// partial file that could not be removed is reported in it beside the reason the download stopped.
/// </summary>
internal sealed class BoundedPackageDownloader(HttpClient client, long limit)
    : NetSparkleUpdater.Interfaces.IUpdateDownloader, IDisposable
{
    #region Private Fields

    private CancellationTokenSource cancel = new();

    #endregion

    #region Properties

    public const string CleanupProblem = "The update download stopped and its partial file could not be removed.";

    public long Limit => limit;

    public bool IsDownloading { get; private set; }

    /// <summary>
    /// Completes, with how it ended, when the latest download has closed its file, removed it if it had to, and raised
    /// its completion.
    /// </summary>
    public Task<DownloadOutcome> Stopped { get; private set; } = Task.FromResult(new DownloadOutcome(null, false));

    #endregion

    #region Interface Implementations

    public event DownloadFromPathToPathEvent? DownloadStarted;

    public event DownloadProgressEvent? DownloadProgressChanged;

    public event global::System.ComponentModel.AsyncCompletedEventHandler? DownloadFileCompleted;

    public async Task DownloadFile(Uri? uri, string downloadFilePath)
    {
        // The token is taken first, so a cancel that arrives once the download is reported as running reaches it.
        var cancellation = cancel.Token;
        var stopped = new TaskCompletionSource<DownloadOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        Stopped = stopped.Task;
        IsDownloading = true;
        var created = new global::System.Runtime.CompilerServices.StrongBox<bool>();
        var ended = new DownloadOutcome(new InvalidOperationException("The update download did not finish."), false);

        try
        {
            // The transfer and everything after it run on the thread pool, never on the caller's thread. A response
            // that is already buffered completes every await at once, so run inline the whole download, its
            // completion, and the update library's signature check would happen inside the library's
            // InitAndBeginDownload, before the caller's time limit starts, on whatever thread called it. ForceYielding
            // matters as much as Task.Run: a transfer that finishes before this await is reached would otherwise
            // continue inline on the caller.
            var outcome = await Task.Run(
                () => Attempt(uri, downloadFilePath, created, cancellation),
                CancellationToken.None).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

            // Only a file this download created is removed. Anything else at the name is left alone.
            ended = outcome.Error is null || !created.Value
                ? outcome
                : outcome.AfterCleanup(Remove(downloadFilePath));
            IsDownloading = false;
            DownloadFileCompleted?.Invoke(this, ended.ToEventArgs());
        }
        finally
        {
            IsDownloading = false;
            stopped.TrySetResult(ended);
        }
    }

    public void CancelDownload()
    {
        // The token of the download in progress stays valid after it is cancelled, so the old source is not disposed
        // while that download may still be reading it.
        var running = Interlocked.Exchange(ref cancel, new());
        running.Cancel();
    }

    /// <summary>The file name comes from the download address. The server is never asked for one.</summary>
    public Task<string?> RetrieveDestinationFileNameAsync(AppCastItem item) => Task.FromResult<string?>(null);

    public void Dispose()
    {
        cancel.Cancel();
        cancel.Dispose();
    }

    #endregion

    #region Private Methods

    /// <summary>Runs one download and says how it ended. It never throws: the event carries the outcome.</summary>
    private async Task<DownloadOutcome> Attempt(
        Uri? uri,
        string path,
        global::System.Runtime.CompilerServices.StrongBox<bool> created,
        CancellationToken cancellation)
    {
        try
        {
            var address = uri ?? throw new InvalidDataException("The update has no download address.");
            DownloadStarted?.Invoke(this, address.ToString(), path);
            await Save(address, path, created, cancellation);
            return new(null, Cancelled: false);
        }
        catch (OperationCanceledException exception) when (cancellation.IsCancellationRequested)
        {
            return new(exception, Cancelled: true);
        }
        catch (Exception exception)
        {
            return new(exception, Cancelled: false);
        }
    }

    /// <summary>
    /// Writes the body to a new file at <paramref name="path"/>. The file is created only if nothing is there, link
    /// included, and shared with no one, so the write can never reach another file.
    /// </summary>
    private async Task Save(
        Uri address,
        string path,
        global::System.Runtime.CompilerServices.StrongBox<bool> created,
        CancellationToken cancellation)
    {
        using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        var declared = response.Content.Headers.ContentLength;

        if (declared > limit)
        {
            throw new InvalidDataException(UpdatePackageTrust.TooLargeProblem);
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellation);
        var handle = UpdatePackageTrust.CreatePackage(path);
        created.Value = true;
        await using var file = new FileStream(handle, FileAccess.Write, 81920, isAsync: true);
        var chunk = new byte[81920];
        var written = 0L;

        while (true)
        {
            var count = await body.ReadAsync(chunk, cancellation);

            if (count == 0)
            {
                return;
            }

            if (written + count > limit)
            {
                throw new InvalidDataException(UpdatePackageTrust.TooLargeProblem);
            }

            await file.WriteAsync(chunk.AsMemory(0, count), cancellation);
            written += count;
            ReportProgress(written, declared ?? 0);
        }
    }

    private void ReportProgress(long written, long declared)
    {
        var total = declared > 0 ? declared : written;
        var percent = total > 0 ? (int)Math.Min(100, written * 100 / total) : 0;
        DownloadProgressChanged?.Invoke(
            this,
            new NetSparkleUpdater.Events.ItemDownloadProgressEventArgs(percent, this, written, total));
    }

    /// <summary>
    /// Removes a partial download and returns why it could not be removed, whatever kind of failure that was, so the
    /// completion is always raised and always carries it.
    /// </summary>
    private static Exception? Remove(string path)
    {
        try
        {
            File.Delete(path);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    #endregion
}


/// <summary>How one package download ended.</summary>
internal readonly record struct DownloadOutcome(Exception? Error, bool Cancelled, Exception? NotRemoved = null)
{
    /// <summary>True when the download finished and its file is on disk.</summary>
    public bool Finished => Error is null && !Cancelled;

    /// <summary>
    /// The outcome once the partial file was removed, or could not be. A file that could not be removed is a failure
    /// that carries both causes, even for a cancelled download, because the library reports no error for a
    /// cancellation and the left file would otherwise go unreported.
    /// </summary>
    public DownloadOutcome AfterCleanup(Exception? notRemoved) => notRemoved is null || Error is null
        ? this
        : new(
            new AggregateException(BoundedPackageDownloader.CleanupProblem, Error, notRemoved),
            Cancelled: false,
            notRemoved);

    public global::System.ComponentModel.AsyncCompletedEventArgs ToEventArgs() => new(Error, Cancelled, null);
}
