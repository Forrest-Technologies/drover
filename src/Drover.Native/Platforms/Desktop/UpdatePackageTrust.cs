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

    private const int AtCurrentDirectory = -100;

    private const int DoNotFollowLinks = 0x100;

    private const uint UserIdMask = 0x8;

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

    /// <summary>The start of the name of each download attempt's own directory.</summary>
    public const string AttemptPrefix = "attempt-";

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
        var seen = settings.Read(name);
        Exception? notSaved = null;

        if (string.IsNullOrWhiteSpace(seen) && settings.Read(LegacyFeedSettingName) is { Length: > 0 } legacy)
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
        using var handle = PackageFiles.OpenForRead(path, FileOptions.None);
        return handle is not null && RandomAccess.GetLength(handle) <= limit;
    }

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
    /// Removes, as far as it can, every earlier attempt directory and stray file in <paramref name="root"/>, except
    /// the attempt directory that holds <paramref name="keep"/>. A link is removed, never what it points to. Returns
    /// what could not be removed, so the caller can log it.
    /// </summary>
    public static List<Exception> RemoveOldAttempts(string root, string? keep)
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

        // An existing directory keeps its mode. A shared or foreign directory is refused, not repaired.
        Directory.CreateDirectory(path, privateMode);
        var mode = File.GetUnixFileMode(path);
        var shared = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if ((mode & privateMode) != privateMode || (mode & shared) != 0 || !OwnedByCurrentUser(path))
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
        var handle = PackageFiles.OpenForRead(path, FileOptions.Asynchronous)
            ?? throw new InvalidDataException(LinkProblem);
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
    /// stopping failed, the timeout and that failure are reported together.
    /// </summary>
    public static async Task<string> Download(
        SparkleUpdater updater,
        AppCastItem item,
        string downloadRoot,
        TimeSpan limit,
        TimeSpan? teardownLimit = null)
    {
        var downloaded = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Finished(AppCastItem item, string path) => downloaded.TrySetResult(path);
        void Failed(AppCastItem item, string? path, Exception exception) => downloaded.TrySetException(exception);
        void Corrupt(AppCastItem item, string path) =>
            downloaded.TrySetException(new InvalidDataException(SignatureProblem));
        void Cancelled(AppCastItem item, string path) => downloaded.TrySetCanceled();

        var downloader = updater.UpdateDownloader as BoundedPackageDownloader;
        var attempt = global::System.IO.Path.GetFullPath(PrepareAttemptDirectory(downloadRoot));
        activeAttempts[attempt] = 0;
        updater.TmpDownloadFilePath = attempt;

        updater.DownloadFinished += Finished;
        updater.DownloadHadError += Failed;
        updater.DownloadCanceled += Cancelled;
        updater.DownloadedFileIsCorrupt += Corrupt;
        updater.DownloadedFileThrewWhileCheckingSignature += Corrupt;
        try
        {
            await updater.InitAndBeginDownload(item);
            return await downloaded.Task.WaitAsync(limit);
        }
        catch (TimeoutException timeout)
        {
            updater.CancelFileDownload();
            var teardown = await Teardown(downloader, teardownLimit ?? TeardownLimit);

            if (teardown is null)
            {
                throw;
            }

            throw new AggregateException(TimeoutProblem, timeout, teardown);
        }
        finally
        {
            updater.DownloadFinished -= Finished;
            updater.DownloadHadError -= Failed;
            updater.DownloadCanceled -= Cancelled;
            updater.DownloadedFileIsCorrupt -= Corrupt;
            updater.DownloadedFileThrewWhileCheckingSignature -= Corrupt;
            _ = ReleaseWhenStopped(attempt, downloader?.Stopped);
        }
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

    /// <summary>True when <paramref name="path"/> names a symbolic link or reparse point, even a broken one.</summary>
    private static bool IsLinkOrReparsePoint(string path)
    {
        var info = new FileInfo(path);
        var attributes = info.Attributes;
        return info.LinkTarget is not null
            || (attributes != (FileAttributes)(-1) && attributes.HasFlag(FileAttributes.ReparsePoint));
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

    /// <summary>
    /// Lets cleanup remove <paramref name="attempt"/> once its download has stopped, however long that takes. The
    /// download task never fails, so this never throws.
    /// </summary>
    private static async Task ReleaseWhenStopped(string attempt, Task<DownloadOutcome>? stopped)
    {
        if (stopped is not null)
        {
            await stopped;
        }

        activeAttempts.TryRemove(attempt, out _);
    }

    /// <summary>
    /// Waits, at most <paramref name="wait"/>, for the download to stop, and returns why it did not stop cleanly: the
    /// failure its completion carried, or a timeout when it did not stop in time. A plain cancellation returns null.
    /// </summary>
    private static async Task<Exception?> Teardown(BoundedPackageDownloader? downloader, TimeSpan wait)
    {
        if (downloader is null)
        {
            return null;
        }

        var stopped = downloader.Stopped;
        await ((Task)stopped.WaitAsync(wait)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (!stopped.IsCompleted)
        {
            return new TimeoutException(TeardownProblem);
        }

        var outcome = await stopped;
        return outcome.Cancelled ? null : outcome.Error;
    }

    private static bool OwnedByCurrentUser(string path) => Owner(path) == CurrentUser();

    private static uint Owner(string path)
    {
        // libc writes the whole status record. 256 bytes covers the macOS and Linux records.
        var buffer = global::System.Runtime.InteropServices.Marshal.AllocHGlobal(256);
        try
        {
            var read = OperatingSystem.IsLinux()
                ? LinuxOwner(path, buffer)
                : DarwinOwner(path, buffer);
            if (read != 0)
            {
                throw new InvalidOperationException("The update download directory could not be checked.");
            }

            // macOS stat.st_uid is at 16. Linux statx.stx_uid is at 20.
            var offset = OperatingSystem.IsLinux() ? 20 : 16;
            return (uint)global::System.Runtime.InteropServices.Marshal.ReadInt32(buffer, offset);
        }
        finally
        {
            global::System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
        }
    }

    private static int DarwinOwner(string path, nint buffer) => DarwinStat(path, buffer);

    private static int LinuxOwner(string path, nint buffer) =>
        LinuxStatX(AtCurrentDirectory, path, DoNotFollowLinks, UserIdMask, buffer);

    private static uint CurrentUser() => CurrentEffectiveUser();

    [global::System.Runtime.InteropServices.DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint CurrentEffectiveUser();

    [global::System.Runtime.InteropServices.DllImport("libc", EntryPoint = "stat")]
    private static extern int DarwinStat(
        [global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string path,
        nint buffer);

    [global::System.Runtime.InteropServices.DllImport("libc", EntryPoint = "statx")]
    private static extern int LinuxStatX(
        int directory,
        [global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mask,
        nint buffer);

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
        using var handle = PackageFiles.OpenForRead(binaryPath, FileOptions.None);
        var package = handle is null ? null : UpdatePackageTrust.ReadPackage(handle, limit);
        return package is null ? ValidationResult.Invalid : inner.VerifySignature(signature, package);
    }

    #endregion
}


/// <summary>Whether the downloaded package may be handed to an installer.</summary>
internal readonly record struct DownloadDecision(DesktopUpdateState State, bool StartInstaller);


/// <summary>Whether a verified feed may be used, and why its version could not be recorded if it could not.</summary>
internal readonly record struct FeedVerdict(bool Accepted, Exception? NotSaved, bool Recorded = true);
