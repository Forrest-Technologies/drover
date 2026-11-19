namespace Drover.Native.Platform;

/// <summary>Checks the release channel carried by this installation and applies only update-signed packages.</summary>
internal sealed class DesktopUpdates : IDesktopUpdates, IDisposable
{
    #region Private Fields

    private const string ChecksSetting = "drover-update-checks";

    private const string PublicKeyMetadata = "DroverUpdatePublicKey";

    private const string PreviewReleases =
        "https://api.github.com/repos/Forrest-Technologies/drover/releases?per_page=20";

    private static readonly TimeSpan downloadLimit = TimeSpan.FromMinutes(10);

    private readonly IDeviceSettings settings;

    private readonly ILogger<DesktopUpdates> logger;

    private readonly HttpClient releases;

    private readonly string installedVersion = ReadInstalledVersion();

    private readonly string publicKey = ReadMetadata(PublicKeyMetadata);

    private SparkleUpdater? updater;

    private AppCastItem? available;

    #endregion

    #region Constructors

    public DesktopUpdates(IDeviceSettings settings, IHttpClientFactory clients, ILogger<DesktopUpdates> logger)
    {
        this.settings = settings;
        this.logger = logger;
        releases = clients.CreateClient("desktop-updates");
        releases.DefaultRequestHeaders.UserAgent.ParseAdd($"Drover/{installedVersion}");
        State = new(!string.IsNullOrWhiteSpace(publicKey), ChecksAreEnabled());
    }

    #endregion

    #region Properties

    public DesktopUpdateState State { get; private set; }

    #endregion

    #region Public Methods

    public event Action? Changed;

    public Task Start() => State is { Supported: true, ChecksEnabled: true }
        ? Check(reportFailure: false)
        : Task.CompletedTask;

    public void SetChecks(bool enabled)
    {
        try
        {
            if (enabled)
            {
                settings.Remove(ChecksSetting);
            }
            else
            {
                settings.Write(ChecksSetting, "off");
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The update preference could not be saved");
            Change(State with
            {
                Phase = DesktopUpdatePhase.Failed,
                Problem = "Drover could not save the update setting.",
            });
            return;
        }

        Change(State with { ChecksEnabled = enabled, Phase = DesktopUpdatePhase.Idle, Problem = null });
        if (enabled)
        {
            _ = Check(reportFailure: false);
        }
    }

    public Task Check() => Check(reportFailure: true);

    public async Task Install()
    {
        if (updater is null || available is null || State.Phase is not DesktopUpdatePhase.Available)
        {
            return;
        }

        var downloaded = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Finished(AppCastItem item, string path) => downloaded.TrySetResult(path);
        void Failed(AppCastItem item, string? path, Exception exception) => downloaded.TrySetException(exception);
        void Corrupt(AppCastItem item, string path) =>
            downloaded.TrySetException(new InvalidDataException("The downloaded update did not have a valid signature."));

        updater.DownloadFinished += Finished;
        updater.DownloadHadError += Failed;
        updater.DownloadedFileIsCorrupt += Corrupt;
        updater.DownloadedFileThrewWhileCheckingSignature += Corrupt;

        try
        {
            Change(State with { Phase = DesktopUpdatePhase.Downloading, Problem = null });
            await updater.InitAndBeginDownload(available);
            var path = await downloaded.Task.WaitAsync(downloadLimit);
            Change(State with { Phase = DesktopUpdatePhase.Installing });
            await updater.InstallUpdate(available, path);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The signed desktop update could not be installed");
            Change(State with { Phase = DesktopUpdatePhase.Failed, Problem = UpdateProblem(exception) });
        }
        finally
        {
            updater.DownloadFinished -= Finished;
            updater.DownloadHadError -= Failed;
            updater.DownloadedFileIsCorrupt -= Corrupt;
            updater.DownloadedFileThrewWhileCheckingSignature -= Corrupt;
        }
    }

    public void Dispose()
    {
        updater?.Dispose();
    }

    #endregion

    #region Private Methods

    private async Task Check(bool reportFailure)
    {
        if (State is not { Supported: true, ChecksEnabled: true }
            || State.Phase is DesktopUpdatePhase.Checking or DesktopUpdatePhase.Downloading or DesktopUpdatePhase.Installing)
        {
            return;
        }

        Change(State with { Phase = DesktopUpdatePhase.Checking, Problem = null });

        try
        {
            var feed = await FindFeed();
            updater?.Dispose();
            updater = BuildUpdater(feed);
            var result = await updater.CheckForUpdatesQuietly(ignoreSkippedVersions: true);
            available = result.Status is UpdateStatus.UpdateAvailable ? result.Updates.FirstOrDefault() : null;
            if (!State.ChecksEnabled)
            {
                return;
            }

            Change(State with
            {
                Phase = available is null ? DesktopUpdatePhase.Idle : DesktopUpdatePhase.Available,
                Version = available?.Version,
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The signed desktop release feed could not be checked");
            Change(State with
            {
                Phase = reportFailure ? DesktopUpdatePhase.Failed : DesktopUpdatePhase.Idle,
                Problem = reportFailure ? UpdateProblem(exception) : null,
            });
        }
    }

    private SparkleUpdater BuildUpdater(string feed)
    {
        var verifier = new Ed25519Checker(SecurityMode.Strict, publicKey, string.Empty, true, 25 * 1024 * 1024);
        var built = new SparkleUpdater(feed, verifier, typeof(App).Assembly.Location)
        {
            CheckServerFileName = false,
            RelaunchAfterUpdate = true,
            ShouldKillParentProcessWhenStartingInstaller = true,
            UserInteractionMode = UserInteractionMode.DownloadNoInstall,
        };
        built.CloseApplication += () => ((App)Application.Current).MainWindow?.Close();

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

    private async Task<string> FindFeed()
    {
        var assetName = FeedAssetName(installedVersion);
        if (DesktopUpdateChannel.FromVersion(installedVersion) is "stable")
        {
            return $"https://github.com/Forrest-Technologies/drover/releases/latest/download/{assetName}";
        }

        using var response = await releases.GetAsync(PreviewReleases, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(content);

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
                    return asset.GetProperty("browser_download_url").GetString()
                        ?? throw new InvalidDataException("The preview feed address is empty.");
                }
            }
        }

        throw new InvalidDataException("No signed preview feed is published yet.");
    }

    private static string FeedAssetName(string version)
    {
        var channel = DesktopUpdateChannel.FromVersion(version) is "preview" ? "-preview" : string.Empty;
        var platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "macos"
                : "linux";
        return $"appcast{channel}-{platform}.xml";
    }

    private bool ChecksAreEnabled()
    {
        try
        {
            return settings.Read(ChecksSetting) is not "off";
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The update preference could not be read");
            return true;
        }
    }

    private void Change(DesktopUpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }

    private static string ReadInstalledVersion() =>
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+', StringSplitOptions.RemoveEmptyEntries)[0]
        ?? typeof(App).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    private static string ReadMetadata(string name) =>
        typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == name)?.Value
        ?? string.Empty;

    private static string UpdateProblem(Exception exception) => exception is InvalidDataException
        ? exception.Message
        : "Drover could not check or install the update. Try again when this device is online.";

    #endregion
}
