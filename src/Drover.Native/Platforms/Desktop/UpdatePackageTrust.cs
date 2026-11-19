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
        string installedVersion,
        CloseApplication? closeApplication)
    {
        var built = new SparkleUpdater(feed, CreateVerifier(publicKey), referenceAssembly)
        {
            CheckServerFileName = false,
            RelaunchAfterUpdate = true,
            ShouldKillParentProcessWhenStartingInstaller = true,
            UserInteractionMode = UserInteractionMode.DownloadNoInstall,
            TmpDownloadFilePath = PrepareDownloadDirectory(downloadDirectory),
        };
        if (closeApplication is not null)
        {
            built.CloseApplication += closeApplication;
        }

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

    public static DownloadDecision Decide(
        string? publicKey,
        string? signature,
        byte[] package,
        UpdateHost host,
        string? downloadLink,
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
                state with { Phase = DesktopUpdatePhase.Manual, Problem = null, DownloadLink = downloadLink },
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
