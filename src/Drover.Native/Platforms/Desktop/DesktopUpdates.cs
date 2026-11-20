namespace Drover.Native.Platform;

/// <summary>Checks the release channel carried by this installation and applies only update-signed packages.</summary>
internal sealed class DesktopUpdates : IDesktopUpdates, IDisposable
{
    #region Private Fields

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
            DesktopUpdateChecks.Store(settings, enabled);
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

        var item = available;
        var active = updater;

        try
        {
            var result = await UpdateInstall.Run(
                new SparkleInstaller(active, item),
                () => UpdatePackageTrust.Download(active, item, downloadLimit),
                publicKey,
                item.DownloadSignature,
                UpdatePackageTrust.CurrentHost,
                item.Version ?? string.Empty,
                () => State,
                Change,
                () => ((App)Application.Current).MainWindow?.Close());
            Change(result);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The signed desktop update could not be installed");
            Change(State with { Phase = DesktopUpdatePhase.Failed, Problem = UpdateProblem(exception) });
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
            if (!State.ChecksEnabled)
            {
                return;
            }

            available = result.Status is UpdateStatus.UpdateAvailable ? result.Updates.FirstOrDefault() : null;
            var verdict = UpdatePackageTrust.AcceptFeed(
                settings,
                DesktopUpdateChannel.FromVersion(installedVersion),
                available?.Version);
            if (verdict.NotSaved is not null)
            {
                logger.LogWarning(verdict.NotSaved, "The newest verified update feed could not be saved");
            }

            if (!verdict.Accepted)
            {
                available = null;
                Change(UpdatePackageTrust.AfterStaleFeed(State, reportFailure));
                return;
            }

            Change(UpdatePackageTrust.AfterCheck(
                State,
                result.Status,
                available?.Version,
                reportFailure,
                () => logger.LogWarning("The signed desktop release feed could not be verified")));
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

    private SparkleUpdater BuildUpdater(string feed) => UpdatePackageTrust.CreateUpdater(
        feed,
        publicKey,
        typeof(App).Assembly.Location,
        UpdatePackageTrust.DownloadDirectory(),
        installedVersion);

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
                    return UpdatePackageTrust.RequireReleaseDownload(asset.GetProperty("browser_download_url").GetString());
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
            return DesktopUpdateChecks.AreEnabled(settings);
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
