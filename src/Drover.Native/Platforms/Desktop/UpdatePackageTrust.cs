namespace Drover.Native.Platform;

/// <summary>The desktop that is about to apply a signed package.</summary>
internal enum UpdateHost
{
    Linux,
    Mac,
    Windows,
}

/// <summary>How a signed desktop package is checked before anything is installed.</summary>
internal static class UpdatePackageTrust
{
    #region Private Fields

    /// <summary>Attempt directories whose download has not stopped yet. No cleanup ever removes one.</summary>
    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, byte> activeAttempts =
        new(StringComparer.Ordinal);

    #endregion

    #region Public Methods

    public const string DownloadPrefix = "https://github.com/Forrest-Technologies/drover/releases/download/";

    public const string FeedProblem = "The signed release feed could not be verified.";

    public const string SignatureProblem = "The downloaded update did not have a valid signature.";

    public const string TooLargeProblem = "The downloaded update is larger than Drover accepts.";

    /// <summary>
    /// The most a downloaded package may be, in bytes. The signature is checked over the whole package, so it is held
    /// in memory once; a download over this, or one replaced by something larger, is refused before it is read.
    /// </summary>
    public const long PackageLimit = 512L * 1024 * 1024;

    public const string UnrecordedProblem = "Drover could not record this update, so it is not offered. Try again.";

    public const string StaleProblem = "This signed feed is older than one this installation has already verified.";

    public const string LinkProblem = "The downloaded update is a link, so Drover does not read it.";

    public const string TimeoutProblem = "The update download took too long and did not stop cleanly.";

    public const string TeardownProblem = "The update download did not stop in time, so its partial file may remain.";

    public const string SettingProblem = "Drover could not save the update setting.";

    /// <summary>The start of the name of each download attempt's own directory.</summary>
    public const string AttemptPrefix = "attempt-";

    /// <summary>
    /// How old an entry of the update folder must be before a check removes it. Another running copy of Drover may
    /// still be using a newer one, and no download or installer handoff takes this long, so the age bounds what is
    /// left behind without any lock shared between copies.
    /// </summary>
    public static readonly TimeSpan OldAttemptAge = TimeSpan.FromHours(24);

    /// <summary>
    /// The longest a download that ran past its time limit is given to stop, close its file, and remove it before the
    /// timeout is reported. A download that has not stopped by then is reported with the timeout, so this wait cannot
    /// hang, and its attempt directory is never given to another download.
    /// </summary>
    public static readonly TimeSpan TeardownLimit = TimeSpan.FromSeconds(10);

    /// <summary>The setting that holds the newest verified feed version for one release channel.</summary>
    public static string FeedSettingName(string channel) => $"drover-update-feed-{channel}";

    /// <summary>
    /// The one record an earlier build kept for both channels. It is moved to the channel record the first time
    /// that channel is checked, so an upgrade keeps the version floor it had.
    /// </summary>
    public const string LegacyFeedSettingName = "drover-update-feed";

    /// <summary>
    /// Checks the version a verified feed offers against the newest one this install has verified for the same
    /// channel. An older offer is refused and the record is left alone. A newer offer is recorded first, and an offer
    /// whose record cannot be saved is refused: without the record, a version signed earlier than this one could be
    /// accepted again after a restart. An offer no newer than the record needs no new record.
    /// </summary>
    public static FeedVerdict AcceptFeed(IDeviceSettings settings, string channel, string? offered)
    {
        var name = FeedSettingName(channel);
        var seen = FloorFor(channel, settings.Read(name));
        var notSaved = default(Exception);

        if (string.IsNullOrWhiteSpace(seen) && FloorFor(channel, settings.Read(LegacyFeedSettingName)) is { } legacy)
        {
            seen = legacy;
            notSaved = MoveLegacyRecord(settings, name, legacy);
        }

        if (IsOlderThanSeen(seen, offered))
        {
            return new FeedVerdict(false, notSaved);
        }

        if (string.IsNullOrWhiteSpace(offered) || (!string.IsNullOrWhiteSpace(seen) && IsSameVersion(seen, offered)))
        {
            return new FeedVerdict(true, notSaved);
        }

        try
        {
            settings.Write(name, offered);
            return new FeedVerdict(true, notSaved);
        }
        catch (Exception exception)
        {
            return new FeedVerdict(false, exception, Recorded: false);
        }
    }

    /// <summary>
    /// <paramref name="recorded"/> when it can be a version floor for <paramref name="channel"/>, otherwise null. A
    /// stable install never takes a preview version as its floor: one recorded by an earlier build, in the shared
    /// record or in its own, is ignored and not imported, so it can never refuse a legitimate stable release.
    /// </summary>
    public static string? FloorFor(string channel, string? recorded) =>
        string.IsNullOrWhiteSpace(recorded)
        || (channel is "stable" && DesktopUpdateChannel.FromVersion(recorded) is not "stable")
            ? null
            : recorded;

    /// <summary>
    /// A newer offer whose version could not be recorded is not offered: nothing is handed to the installer, and a
    /// manual check says why. An automatic check offers nothing and says nothing.
    /// </summary>
    public static DesktopUpdateState AfterUnrecordedFeed(DesktopUpdateState state, bool manual) => manual
        ? state with { Phase = DesktopUpdatePhase.Failed, Version = null, Problem = UnrecordedProblem }
        : state with { Phase = DesktopUpdatePhase.Idle, Version = null, Problem = null };

    /// <summary>A stale feed is shown only for a manual check. An automatic check offers nothing and says nothing.</summary>
    public static DesktopUpdateState AfterStaleFeed(DesktopUpdateState state, bool manual) => manual
        ? state with { Phase = DesktopUpdatePhase.Failed, Version = null, Problem = StaleProblem }
        : state with { Phase = DesktopUpdatePhase.Idle, Version = null, Problem = null };

    /// <summary>
    /// True when <paramref name="offered"/> is older than a signed feed this install has already verified.
    /// A missing version is not a replay.
    /// </summary>
    public static bool IsOlderThanSeen(string? seen, string? offered)
    {
        if (string.IsNullOrWhiteSpace(seen) || string.IsNullOrWhiteSpace(offered))
        {
            return false;
        }

        return SemVerLike.Parse(offered).CompareTo(SemVerLike.Parse(seen)) < 0;
    }

    public static UpdateHost CurrentHost => OperatingSystem.IsWindows()
        ? UpdateHost.Windows
        : OperatingSystem.IsMacOS()
            ? UpdateHost.Mac
            : UpdateHost.Linux;

    public static bool StartsPrivilegedInstaller(UpdateHost host) => host is UpdateHost.Windows or UpdateHost.Mac;

    public static Ed25519Checker CreateVerifier(string? publicKey)
    {
        var key = string.IsNullOrWhiteSpace(publicKey) ? null : publicKey.Trim();
        return new Ed25519Checker(SecurityMode.Strict, key, null, true, 25 * 1024 * 1024);
    }

    /// <summary>
    /// The updater for one check. Every entry from another release channel is removed before the library chooses an
    /// update, so it never reaches the version record or the installer. The feed, its signature, and the package are
    /// read through <paramref name="client"/> with fixed byte limits, and every file the library checks is read
    /// through <see cref="BoundedFileVerifier"/>, so no package over <paramref name="packageLimit"/> is read whole.
    /// </summary>
    public static SparkleUpdater CreateUpdater(
        string feed,
        string? publicKey,
        string referenceAssembly,
        string downloadDirectory,
        string installedVersion,
        HttpClient client,
        long packageLimit = PackageLimit)
    {
        var verifier = new BoundedFileVerifier(CreateVerifier(publicKey), packageLimit);
        var built = new SparkleUpdater(feed, verifier, referenceAssembly)
        {
            AppCastDataDownloader = new BoundedFeedDownloader(
                client,
                UpdateReads.FeedLimit,
                UpdateReads.ReadDeadline),
            UpdateDownloader = new BoundedPackageDownloader(client, packageLimit),
            CheckServerFileName = false,
            RelaunchAfterUpdate = true,
            ShouldKillParentProcessWhenStartingInstaller = true,
            UserInteractionMode = UserInteractionMode.DownloadNoInstall,
            TmpDownloadFilePath = PrepareDownloadDirectory(downloadDirectory),
        };
        var channel = DesktopUpdateChannel.FromVersion(installedVersion);
        built.AppCastHelper.AppCastFilter = new ReleaseChannelFilter(channel);
        return built;
    }

    /// <summary>
    /// The newest update a check found that belongs to <paramref name="channel"/>, or null. The filter has already
    /// removed every other channel; this holds the version record to the same rule if it ever did not.
    /// </summary>
    public static AppCastItem? Offer(UpdateInfo result, string channel) =>
        result.Status is UpdateStatus.UpdateAvailable
            ? result.Updates.FirstOrDefault(item => ReleaseChannelFilter.IsOnChannel(item, channel))
            : null;

    /// <summary>
    /// True when <paramref name="path"/> is a regular file, not a link or reparse point, no larger than
    /// <paramref name="limit"/>. The type and the size come from the opened file.
    /// </summary>
    public static bool IsWithinLimit(string path, long limit)
    {
        if (IsLinkOrReparsePoint(path))
        {
            return false;
        }

        using var handle = OpenPackage(path, FileOptions.None);
        return RandomAccess.GetLength(handle) <= limit;
    }

    /// <summary>
    /// Opens a downloaded package for reading. A link or reparse point at the path is refused first. The length and
    /// every byte read come from this one handle. A process of the same user that swaps the name between the check and
    /// the open is outside the threat model (docs/desktop-updates.md).
    /// </summary>
    public static Microsoft.Win32.SafeHandles.SafeFileHandle OpenPackage(string path, FileOptions options) =>
        IsLinkOrReparsePoint(path)
            ? throw new InvalidDataException(LinkProblem)
            : File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, options);

    /// <summary>
    /// Creates a new package file for writing, shared with no one. The create fails if anything, a link included, is
    /// already at the path, so a write never goes through to another file.
    /// </summary>
    public static Microsoft.Win32.SafeHandles.SafeFileHandle CreatePackage(string path) =>
        File.OpenHandle(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileOptions.Asynchronous);

    /// <summary>True when <paramref name="path"/> names a symbolic link or reparse point, even a broken one.</summary>
    public static bool IsLinkOrReparsePoint(string path)
    {
        var info = new FileInfo(path);
        var attributes = info.Attributes;
        return info.LinkTarget is not null
            || (attributes != (FileAttributes)(-1) && attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    /// <summary>The problem shown for a failed check or install: why data was refused, or a general reason.</summary>
    public static string ProblemFor(Exception exception) => exception is InvalidDataException
        ? exception.Message
        : "Drover could not check or install the update. Try again when this device is online.";

    /// <summary>
    /// The state after the checks setting could not be saved. A download or install in progress is left as it is;
    /// anything else shows why the setting did not change.
    /// </summary>
    public static DesktopUpdateState AfterSettingNotSaved(DesktopUpdateState state) =>
        state.Phase is DesktopUpdatePhase.Downloading or DesktopUpdatePhase.Installing
            ? state
            : state with { Phase = DesktopUpdatePhase.Failed, Problem = SettingProblem };

    /// <summary>
    /// The state after the checks setting changes. A download or install in progress keeps its phase, so no new
    /// check can start beside it. Any other state returns to idle.
    /// </summary>
    public static DesktopUpdateState AfterChecksSwitch(DesktopUpdateState state, bool enabled) =>
        state.Phase is DesktopUpdatePhase.Downloading or DesktopUpdatePhase.Installing
            ? state with { ChecksEnabled = enabled }
            : state with { ChecksEnabled = enabled, Phase = DesktopUpdatePhase.Idle, Problem = null };

    /// <summary>
    /// A new directory for one download attempt, inside <paramref name="root"/> and private to this user. No other
    /// attempt, from this updater or another, ever uses it, so an attempt that is still stopping can never touch the
    /// file of the next one.
    /// </summary>
    public static string PrepareAttemptDirectory(string root)
    {
        PrepareDownloadDirectory(root);
        var attempt = global::System.IO.Path.Combine(root, $"{AttemptPrefix}{Guid.NewGuid():N}");

        if (global::System.IO.Path.Exists(attempt) || IsLinkOrReparsePoint(attempt))
        {
            throw new InvalidOperationException("The update download attempt directory already exists.");
        }

        return PrepareDownloadDirectory(attempt);
    }

    /// <summary>
    /// Removes, as far as it can, entries of <paramref name="root"/> last written more than <see cref="OldAttemptAge"/>
    /// before <paramref name="now"/>, and any link, which is removed and never followed. It never removes an attempt
    /// of this process whose download has not stopped, or the attempt that holds <paramref name="keep"/>. Returns what
    /// could not be removed, so the caller can log it.
    /// </summary>
    public static List<Exception> RemoveOldAttempts(string root, string? keep, DateTimeOffset now)
    {
        List<Exception> problems = [];

        if (!Directory.Exists(root) || IsLinkOrReparsePoint(root))
        {
            return problems;
        }

        var kept = keep is null
            ? null
            : global::System.IO.Path.GetDirectoryName(global::System.IO.Path.GetFullPath(keep));

        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var full = global::System.IO.Path.GetFullPath(entry);

            if (string.Equals(full, kept, StringComparison.Ordinal) || activeAttempts.ContainsKey(full))
            {
                continue;
            }

            if (!IsLinkOrReparsePoint(entry) && now - File.GetLastWriteTimeUtc(entry) <= OldAttemptAge)
            {
                continue;
            }

            if (RemoveEntry(entry) is { } problem)
            {
                problems.Add(problem);
            }
        }

        return problems;
    }

    public static string DownloadDirectory() => global::System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Drover",
        "updates");

    public static string PrepareDownloadDirectory(string path)
    {
        if (IsLinkOrReparsePoint(path))
        {
            throw new InvalidOperationException("The update download directory is a link.");
        }

        var privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return path;
        }

        // An existing directory keeps its mode. A shared directory is refused, not repaired.
        Directory.CreateDirectory(path, privateMode);
        var mode = File.GetUnixFileMode(path);
        var shared = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if ((mode & privateMode) != privateMode || (mode & shared) != 0)
        {
            throw new InvalidOperationException("The update download directory is not private.");
        }

        return path;
    }

    public static string RequireReleaseDownload(string? address)
    {
        if (address is null
            || !address.StartsWith(DownloadPrefix, StringComparison.Ordinal)
            || !Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The preview feed address is not a release download.");
        }

        return address;
    }

    /// <summary>
    /// The address of the signed preview feed named <paramref name="assetName"/> in the release list at
    /// <paramref name="releases"/>. The list is read through <see cref="UpdateReads.Read"/>, at most
    /// <see cref="UpdateReads.ReleaseListLimit"/> bytes within <paramref name="deadline"/>.
    /// </summary>
    public static async Task<string> FindPreviewFeed(
        HttpClient client,
        string releases,
        string assetName,
        TimeSpan deadline)
    {
        var list = await UpdateReads.Read(client, releases, UpdateReads.ReleaseListLimit, deadline);
        using var document = JsonDocument.Parse(list);

        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (!release.GetProperty("prerelease").GetBoolean())
            {
                continue;
            }

            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() == assetName)
                {
                    return RequireReleaseDownload(asset.GetProperty("browser_download_url").GetString());
                }
            }
        }

        throw new InvalidDataException("No signed preview feed is published yet.");
    }

    public static DesktopUpdateState AfterCheck(
        DesktopUpdateState state,
        UpdateStatus status,
        string? version,
        bool manual,
        Action warn)
    {
        if (status is UpdateStatus.CouldNotDetermine)
        {
            warn();
            return manual
                ? state with { Phase = DesktopUpdatePhase.Failed, Version = null, Problem = FeedProblem }
                : state with { Phase = DesktopUpdatePhase.Idle, Version = null, Problem = null };
        }

        if (status is UpdateStatus.UpdateAvailable && !string.IsNullOrWhiteSpace(version))
        {
            return state with { Phase = DesktopUpdatePhase.Available, Version = version, Problem = null };
        }

        return state with { Phase = DesktopUpdatePhase.Idle, Version = null, Problem = null };
    }

    /// <summary>
    /// Reads the whole package, at most <paramref name="limit"/> bytes, and nothing when it is more: a stream that says
    /// it is longer than the limit is refused without a byte read, and one that grows past what it said stops at the
    /// next byte. The length is read once, and the same value that was checked sizes the buffer, so a stream that
    /// reports a different length the next time cannot make the buffer larger than the limit. Returns null for a
    /// package over the limit.
    /// </summary>
    public static async Task<byte[]?> ReadPackage(Stream stream, long limit)
    {
        var length = stream.Length;

        if (length > limit)
        {
            return null;
        }

        var package = new byte[length];
        var read = 0;

        while (read < package.Length)
        {
            var count = await stream.ReadAsync(package.AsMemory(read));

            if (count == 0)
            {
                return package[..read];
            }

            read += count;
        }

        // A file that grew since its length was read is not the file that was measured.
        return stream.ReadByte() >= 0 ? null : package;
    }

    /// <summary>
    /// The package at <paramref name="path"/>, read through one opened handle by
    /// <see cref="ReadPackage(Stream, long)"/>, or null when it is over the limit. A link is refused.
    /// </summary>
    public static async Task<byte[]?> ReadPackageFile(string path, long limit)
    {
        var handle = OpenPackage(path, FileOptions.Asynchronous);
        await using var file = new FileStream(handle, FileAccess.Read, 81920, isAsync: true);
        return await ReadPackage(file, limit);
    }

    /// <summary>
    /// The same bounded read as <see cref="ReadPackage(Stream, long)"/>, on an opened handle, for a caller that cannot
    /// wait asynchronously. The length is read from the handle once and sizes the buffer; a file that grew since is
    /// refused after one more byte.
    /// </summary>
    public static byte[]? ReadPackage(Microsoft.Win32.SafeHandles.SafeFileHandle handle, long limit)
    {
        var length = RandomAccess.GetLength(handle);

        if (length > limit)
        {
            return null;
        }

        var package = new byte[length];
        var read = 0;

        while (read < package.Length)
        {
            var count = RandomAccess.Read(handle, package.AsSpan(read), read);

            if (count == 0)
            {
                return package[..read];
            }

            read += count;
        }

        var extra = new byte[1];
        return RandomAccess.Read(handle, extra, read) > 0 ? null : package;
    }

    public static DownloadDecision Decide(
        string? publicKey,
        string? signature,
        byte[] package,
        UpdateHost host,
        string? verifiedPackage,
        DesktopUpdateState state)
    {
        var verdict = CreateVerifier(publicKey).VerifySignature(signature ?? string.Empty, package);
        if (verdict is not ValidationResult.Valid)
        {
            return new DownloadDecision(
                state with { Phase = DesktopUpdatePhase.Failed, Problem = SignatureProblem },
                false);
        }

        if (!StartsPrivilegedInstaller(host))
        {
            return new DownloadDecision(
                state with { Phase = DesktopUpdatePhase.Manual, Problem = null, VerifiedPackage = verifiedPackage },
                false);
        }

        return new DownloadDecision(state with { Phase = DesktopUpdatePhase.Installing, Problem = null }, true);
    }

    /// <summary>
    /// Downloads <paramref name="item"/> into a new attempt directory of its own inside
    /// <paramref name="downloadRoot"/>, and returns the verified file. A download that runs past
    /// <paramref name="limit"/> is cancelled. Its timeout is reported once the download has stopped, waiting at most
    /// <paramref name="teardownLimit"/>, or <see cref="TeardownLimit"/>, for that. If it did not stop in time, or
    /// stopping failed, the timeout and that failure are reported together. A download that stops only after that
    /// wait has its final outcome logged to <paramref name="logger"/> with its attempt directory, so a late cleanup
    /// failure is never lost.
    /// </summary>
    public static async Task<string> Download(
        SparkleUpdater updater,
        AppCastItem item,
        string downloadRoot,
        TimeSpan limit,
        ILogger logger,
        TimeSpan? teardownLimit = null,
        TimeProvider? time = null)
    {
        var clock = time ?? TimeProvider.System;
        var wait = teardownLimit ?? TeardownLimit;
        var downloader = updater.UpdateDownloader as BoundedPackageDownloader;
        var attempt = global::System.IO.Path.GetFullPath(PrepareAttemptDirectory(downloadRoot));
        activeAttempts[attempt] = 0;
        updater.TmpDownloadFilePath = attempt;

        // The update library's verdict on this attempt. Callbacks for any other attempt are ignored.
        var verdict = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Finished(AppCastItem item, string path) => _ = IsInAttempt(path, attempt) && verdict.TrySetResult(path);
        void Failed(AppCastItem item, string? path, Exception exception) =>
            _ = IsInAttempt(path, attempt) && verdict.TrySetException(exception);
        void Corrupt(AppCastItem item, string path) =>
            _ = IsInAttempt(path, attempt) && verdict.TrySetException(new InvalidDataException(SignatureProblem));
        void Cancelled(AppCastItem item, string path) => _ = IsInAttempt(path, attempt) && verdict.TrySetCanceled();

        void Unsubscribe()
        {
            updater.DownloadFinished -= Finished;
            updater.DownloadHadError -= Failed;
            updater.DownloadCanceled -= Cancelled;
            updater.DownloadedFileIsCorrupt -= Corrupt;
            updater.DownloadedFileThrewWhileCheckingSignature -= Corrupt;
        }

        updater.DownloadFinished += Finished;
        updater.DownloadHadError += Failed;
        updater.DownloadCanceled += Cancelled;
        updater.DownloadedFileIsCorrupt += Corrupt;
        updater.DownloadedFileThrewWhileCheckingSignature += Corrupt;
        var timedOut = false;
        try
        {
            await updater.InitAndBeginDownload(item);
            return await verdict.Task.WaitAsync(limit, clock);
        }
        catch (TimeoutException timeout)
        {
            // The deadline won, so the timeout is the result. The verdict may still arrive while the download stops;
            // the callbacks stay subscribed until then, and the verdict is reported with the teardown, never lost.
            timedOut = true;
            updater.CancelFileDownload();
            var teardown = downloader is null
                ? null
                : await ObserveTeardown(downloader.Stopped, verdict.Task, wait, clock, attempt, logger);

            if (teardown is null)
            {
                throw;
            }

            throw new AggregateException(TimeoutProblem, timeout, teardown);
        }
        catch (Exception)
        {
            // A rejected package (a bad signature, a corrupt file) or any other failed download leaves nothing behind.
            await RemoveRejectedAttempt(attempt, downloader, wait, clock, logger);
            throw;
        }
        finally
        {
            if (timedOut && downloader is not null)
            {
                _ = UnsubscribeWhenStopped(downloader.Stopped, Unsubscribe);
            }
            else
            {
                Unsubscribe();
            }

            _ = ReleaseWhenStopped(attempt, downloader?.Stopped);
        }
    }

    /// <summary>
    /// After a download ran past its time limit and was cancelled, waits at most <paramref name="wait"/> for
    /// <paramref name="stopped"/>. The update library checks a finished file inside the download's completion, before
    /// <paramref name="stopped"/> completes, so once it completes nothing uses the attempt any more. A timed-out
    /// attempt's path is never returned, so it is never a kept package, and its directory is always removed then,
    /// whatever the library decided about the file; a directory that cannot be removed is logged. When the download
    /// stopped in time, returns why it did not stop cleanly, or null. When the deadline won, returns the teardown
    /// timeout at once and removes and logs the attempt when <paramref name="stopped"/> completes later. Which of the
    /// two happened is decided once, by <see cref="StoppedWithin"/>. The update library's <paramref name="verdict"/>
    /// on the file, which it gives before <paramref name="stopped"/> completes, is observed either way: a rejection
    /// in time is returned with the download's own failure, and a later one is logged.
    /// </summary>
    public static async Task<Exception?> ObserveTeardown(
        Task<DownloadOutcome> stopped,
        Task<string> verdict,
        TimeSpan wait,
        TimeProvider time,
        string attempt,
        ILogger logger)
    {
        if (!await StoppedWithin(stopped, wait, time))
        {
            _ = RemoveAfterLateStop(stopped, verdict, attempt, logger);
            return new TimeoutException(TeardownProblem);
        }

        var outcome = await stopped;
        var rejected = FaultOf(verdict);
        RemoveTimedOutAttempt(attempt, outcome, rejected, late: false, logger);
        List<Exception> causes = [.. FailureOf(outcome), .. rejected is null ? [] : new[] { rejected }];
        return causes switch
        {
            [] => null,
            [var only] => only,
            _ => new AggregateException(TeardownProblem, causes),
        };
    }

    /// <summary>
    /// True when <paramref name="stopped"/> completes before <paramref name="wait"/> passes on
    /// <paramref name="time"/>, false when the time passes first. The answer is whichever of the two settles one
    /// decision first, taken once; the task is not looked at again afterwards.
    /// </summary>
    public static async Task<bool> StoppedWithin(Task<DownloadOutcome> stopped, TimeSpan wait, TimeProvider time)
    {
        var decided = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = time.CreateTimer(
            _ => decided.TrySetResult(false),
            null,
            wait,
            Timeout.InfiniteTimeSpan);
        // The stop side is settled on the thread that completes the download, whatever the caller's synchronization
        // context is: an await would queue it to a busy caller, or to the thread pool, and let the deadline win a race
        // it lost. This one continuation is why ContinueWith is used here rather than await.
        _ = stopped.ContinueWith(
            _ => decided.TrySetResult(true),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return await decided.Task;
    }

    #endregion

    #region Private Methods

    private static bool IsSameVersion(string seen, string offered) =>
        SemVerLike.Parse(offered).CompareTo(SemVerLike.Parse(seen)) == 0;

    private static Exception? MoveLegacyRecord(IDeviceSettings settings, string name, string legacy)
    {
        try
        {
            settings.Write(name, legacy);
            settings.Remove(LegacyFeedSettingName);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    /// <summary>Removes one entry of the download directory. A link is removed, never what it points to.</summary>
    private static Exception? RemoveEntry(string entry)
    {
        try
        {
            var link = IsLinkOrReparsePoint(entry);

            if (link && OperatingSystem.IsWindows() && Directory.Exists(entry))
            {
                Directory.Delete(entry);
            }
            else if (!link && Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }

            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    /// <summary>Lets cleanup remove <paramref name="attempt"/> once its download has stopped, however late.</summary>
    private static async Task ReleaseWhenStopped(string attempt, Task<DownloadOutcome>? stopped)
    {
        if (stopped is not null)
        {
            await stopped;
        }

        activeAttempts.TryRemove(attempt, out _);
    }

    /// <summary>Removes a timed-out attempt once its download stopped after the deadline, and logs it.</summary>
    private static async Task RemoveAfterLateStop(
        Task<DownloadOutcome> stopped,
        Task<string> verdict,
        string attempt,
        ILogger logger)
    {
        var outcome = await stopped;
        RemoveTimedOutAttempt(attempt, outcome, FaultOf(verdict), late: true, logger);
    }

    private static async Task UnsubscribeWhenStopped(Task<DownloadOutcome> stopped, Action unsubscribe)
    {
        await stopped;
        unsubscribe();
    }

    /// <summary>True when the update library's callback is about a file in <paramref name="attempt"/>.</summary>
    private static bool IsInAttempt(string? path, string attempt)
    {
        var separator = global::System.IO.Path.DirectorySeparatorChar;
        return path is null
            || global::System.IO.Path.GetFullPath(path).StartsWith($"{attempt}{separator}", StringComparison.Ordinal);
    }

    /// <summary>The exception a settled verdict failed with, read so it is never an unobserved task fault.</summary>
    private static Exception? FaultOf(Task<string> verdict) => verdict.Exception switch
    {
        null => null,
        { InnerExceptions: [var only] } => only,
        var several => several,
    };

    /// <summary>What a download's own outcome reports as a failure: never a plain cancellation.</summary>
    private static Exception[] FailureOf(DownloadOutcome outcome) =>
        outcome is { Cancelled: false, Error: { } error } ? [error] : [];

    /// <summary>
    /// Removes the directory of a timed-out attempt. A directory that cannot be removed is logged as a warning with
    /// every cause. A late stop is logged when its attempt was removed, with any failure the download or the update
    /// library reported, and says so when a partial file that could not be removed then was removed with the attempt.
    /// </summary>
    private static void RemoveTimedOutAttempt(
        string attempt,
        DownloadOutcome outcome,
        Exception? rejected,
        bool late,
        ILogger logger)
    {
        var ended = outcome.Finished ? "finished" : outcome.Cancelled ? "was cancelled" : "failed";
        List<Exception> causes = [.. FailureOf(outcome), .. rejected is null ? [] : new[] { rejected }];
        var notRemoved = RemoveDirectory(attempt);

        if (notRemoved is not null)
        {
            logger.LogWarning(
                new AggregateException(TeardownProblem, [.. causes, notRemoved]),
                "A timed-out update download in {Attempt} {Ended} and its attempt could not be removed",
                attempt,
                ended);
            return;
        }

        if (!late)
        {
            return;
        }

        var reported = causes.Count == 0 ? null : new AggregateException(TeardownProblem, causes);
        if (outcome.NotRemoved is not null)
        {
            logger.LogInformation(
                reported,
                "A timed-out update download in {Attempt} {Ended} late; its stuck partial file went with its attempt",
                attempt,
                ended);
        }
        else
        {
            logger.LogInformation(
                reported,
                "A timed-out update download in {Attempt} {Ended} after its deadline and its attempt was removed",
                attempt,
                ended);
        }
    }

    /// <summary>Removes a directory and everything in it, and returns why it could not be, if it could not.</summary>
    private static Exception? RemoveDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    /// <summary>
    /// Removes the attempt of a download that failed or was rejected, once the update library and the downloader are
    /// done with it, waiting at most <paramref name="wait"/> for that. One that is not done in time, or cannot be
    /// removed, is logged and left for a later check to remove.
    /// </summary>
    private static async Task RemoveRejectedAttempt(
        string attempt,
        BoundedPackageDownloader? downloader,
        TimeSpan wait,
        TimeProvider time,
        ILogger logger)
    {
        if (downloader is not null && !await StoppedWithin(downloader.Stopped, wait, time))
        {
            logger.LogWarning("A rejected update download in {Attempt} did not stop, so it is left for now", attempt);
            return;
        }

        if (RemoveDirectory(attempt) is { } notRemoved)
        {
            logger.LogWarning(notRemoved, "A rejected update download in {Attempt} could not be removed", attempt);
        }
    }

    #endregion
}


/// <summary>
/// Keeps only entries for one release channel that are newer than the installed version, newest first. An entry is on
/// a channel when the channel its version names matches and any channel the entry declares matches too, so a preview
/// entry is refused by a stable install whichever of the two marks it, and a stable entry by a preview install.
/// </summary>
internal sealed class ReleaseChannelFilter(string channel) : NetSparkleUpdater.Interfaces.IAppCastFilter
{
    #region Properties

    public string Channel => channel;

    #endregion

    #region Public Methods

    public static bool IsOnChannel(AppCastItem item, string channel) =>
        !string.IsNullOrWhiteSpace(item.Version)
        && DesktopUpdateChannel.FromVersion(item.Version) == channel
        && (string.IsNullOrWhiteSpace(item.Channel)
            || string.Equals(item.Channel.Trim(), channel, StringComparison.OrdinalIgnoreCase));

    #endregion

    #region Interface Implementations

    public IEnumerable<AppCastItem> GetFilteredAppCastItems(SemVerLike installed, IEnumerable<AppCastItem> items) =>
        items
            .Where(item => IsOnChannel(item, channel) && SemVerLike.Parse(item.Version).CompareTo(installed) > 0)
            .OrderByDescending(item => item.SemVerLikeVersion);

    #endregion
}


/// <summary>
/// Checks a file's signature for the update library with the strict Ed25519 checker, reading the file through one
/// opened handle and at most <paramref name="limit"/> bytes. The library checks a file in three places (a file already
/// downloaded, a fresh download, and again before the installer starts); every one of them comes here. A link, a
/// reparse point, or a file over the limit is invalid and is not read.
/// </summary>
internal sealed class BoundedFileVerifier(NetSparkleUpdater.Interfaces.ISignatureVerifier inner, long limit)
    : NetSparkleUpdater.Interfaces.ISignatureVerifier
{
    #region Properties

    public long Limit => limit;

    /// <summary>Always the checker's strict mode. A request to change it is refused.</summary>
    public SecurityMode SecurityMode
    {
        get => inner.SecurityMode;
        set
        {
            if (value != inner.SecurityMode)
            {
                throw new NotSupportedException("Drover checks update signatures only in strict mode.");
            }
        }
    }

    #endregion

    #region Interface Implementations

    public bool HasValidKeyInformation() => inner.HasValidKeyInformation();

    public ValidationResult VerifySignature(string signature, byte[] dataToVerify) =>
        inner.VerifySignature(signature, dataToVerify);

    public ValidationResult VerifySignatureOfString(string signature, string data) =>
        inner.VerifySignatureOfString(signature, data);

    public ValidationResult VerifySignatureOfFile(string signature, string binaryPath)
    {
        if (UpdatePackageTrust.IsLinkOrReparsePoint(binaryPath))
        {
            return ValidationResult.Invalid;
        }

        using var handle = UpdatePackageTrust.OpenPackage(binaryPath, FileOptions.None);
        var package = UpdatePackageTrust.ReadPackage(handle, limit);
        return package is null ? ValidationResult.Invalid : inner.VerifySignature(signature, package);
    }

    #endregion
}


/// <summary>Whether the downloaded package may be handed to an installer.</summary>
internal readonly record struct DownloadDecision(DesktopUpdateState State, bool StartInstaller);


/// <summary>Whether a verified feed may be used, and why its version could not be recorded if it could not.</summary>
internal readonly record struct FeedVerdict(bool Accepted, Exception? NotSaved, bool Recorded = true);
