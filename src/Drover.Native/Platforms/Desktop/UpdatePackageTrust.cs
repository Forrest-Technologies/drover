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

    public static SparkleUpdater CreateUpdater(
        string feed,
        string? publicKey,
        string referenceAssembly,
        string downloadDirectory,
        string installedVersion)
    {
        var built = new SparkleUpdater(feed, CreateVerifier(publicKey), referenceAssembly)
        {
            CheckServerFileName = false,
            RelaunchAfterUpdate = true,
            ShouldKillParentProcessWhenStartingInstaller = true,
            UserInteractionMode = UserInteractionMode.DownloadNoInstall,
            TmpDownloadFilePath = PrepareDownloadDirectory(downloadDirectory),
        };
        if (DesktopUpdateChannel.FromVersion(installedVersion) is "preview")
        {
            built.AppCastHelper.AppCastFilter = new ChannelAppCastFilter
            {
                ChannelSearchNames = ["preview"],
                KeepItemsWithNoChannelInfo = false,
            };
        }

        return built;
    }

    public static string DownloadDirectory() => global::System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Drover",
        "updates");

    public static string PrepareDownloadDirectory(string path)
    {
        if (IsLink(path))
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

    /// <summary>The package at <paramref name="path"/> through <see cref="ReadPackage"/>, or null when it is over the limit.</summary>
    public static async Task<byte[]?> ReadPackageFile(string path, long limit)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return await ReadPackage(file, limit);
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

    public static async Task<string> Download(SparkleUpdater updater, AppCastItem item, TimeSpan limit)
    {
        var downloaded = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Finished(AppCastItem item, string path) => downloaded.TrySetResult(path);
        void Failed(AppCastItem item, string? path, Exception exception) => downloaded.TrySetException(exception);
        void Corrupt(AppCastItem item, string path) => downloaded.TrySetException(new InvalidDataException(SignatureProblem));

        updater.DownloadFinished += Finished;
        updater.DownloadHadError += Failed;
        updater.DownloadedFileIsCorrupt += Corrupt;
        updater.DownloadedFileThrewWhileCheckingSignature += Corrupt;
        try
        {
            await updater.InitAndBeginDownload(item);
            return await downloaded.Task.WaitAsync(limit);
        }
        finally
        {
            updater.DownloadFinished -= Finished;
            updater.DownloadHadError -= Failed;
            updater.DownloadedFileIsCorrupt -= Corrupt;
            updater.DownloadedFileThrewWhileCheckingSignature -= Corrupt;
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

    private static bool IsLink(string path) =>
        global::System.IO.Path.Exists(path)
        && (File.ResolveLinkTarget(path, returnFinalTarget: false) is not null
            || Directory.ResolveLinkTarget(path, returnFinalTarget: false) is not null);

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

/// <summary>Whether the downloaded package may be handed to an installer.</summary>
internal readonly record struct DownloadDecision(DesktopUpdateState State, bool StartInstaller);


/// <summary>Whether a verified feed may be used, and why its version could not be recorded if it could not.</summary>
internal readonly record struct FeedVerdict(bool Accepted, Exception? NotSaved, bool Recorded = true);
