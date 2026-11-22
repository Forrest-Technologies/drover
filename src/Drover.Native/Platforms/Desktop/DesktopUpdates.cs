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

    private readonly UpdateCheckFlow checks;

    #endregion

    #region Constructors

    public DesktopUpdates(IDeviceSettings settings, IHttpClientFactory clients, ILogger<DesktopUpdates> logger)
    {
        this.settings = settings;
        this.logger = logger;
        releases = clients.CreateClient("desktop-updates");
        releases.DefaultRequestHeaders.UserAgent.ParseAdd($"Drover/{installedVersion}");
        State = new(!string.IsNullOrWhiteSpace(publicKey), ChecksAreEnabled());
        checks = new(
            settings,
            DesktopUpdateChannel.FromVersion(installedVersion),
            FindFeed,
            BuildUpdater,
            RemoveOldAttempts,
            () => State,
            Change,
            logger);
    }

    #endregion

    #region Properties

    public DesktopUpdateState State { get; private set; }

    #endregion

    #region Public Methods

    public event Action? Changed;

    public Task Start() => State is { Supported: true, ChecksEnabled: true }
        ? checks.Check(reportFailure: false)
        : Task.CompletedTask;

    public void SetChecks(bool enabled) => checks.SetChecks(enabled);

    public Task Check() => checks.Check(reportFailure: true);

    public async Task Install()
    {
        if (checks.Updater is not { } active || checks.Available is not { } item
            || State.Phase is not DesktopUpdatePhase.Available)
        {
            return;
        }

        try
        {
            var result = await UpdateInstall.Run(
                new SparkleInstaller(active, item),
                () => UpdatePackageTrust.Download(
                    active,
                    item,
                    UpdatePackageTrust.DownloadDirectory(),
                    downloadLimit,
                    logger),
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
            Change(State with
            {
                Phase = DesktopUpdatePhase.Failed,
                Problem = UpdatePackageTrust.ProblemFor(exception),
            });
        }
    }

    public void Dispose()
    {
        checks.Dispose();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Removes download attempts more than a day old. A check never runs during a download or install, and the
    /// attempt that holds a verified package still shown to the person is kept.
    /// </summary>
    private void RemoveOldAttempts()
    {
        var problems = UpdatePackageTrust.RemoveOldAttempts(
            UpdatePackageTrust.DownloadDirectory(),
            State.VerifiedPackage,
            DateTimeOffset.UtcNow);

        foreach (var problem in problems)
        {
            logger.LogDebug(problem, "An earlier update download could not be removed");
        }
    }

    private SparkleUpdater BuildUpdater(string feed) => UpdatePackageTrust.CreateUpdater(
        feed,
        publicKey,
        typeof(App).Assembly.Location,
        UpdatePackageTrust.DownloadDirectory(),
        installedVersion,
        releases);

    private async Task<string> FindFeed()
    {
        var assetName = FeedAssetName(installedVersion);
        if (DesktopUpdateChannel.FromVersion(installedVersion) is "stable")
        {
            return $"https://github.com/Forrest-Technologies/drover/releases/latest/download/{assetName}";
        }

        return await UpdatePackageTrust.FindPreviewFeed(
            releases,
            PreviewReleases,
            assetName,
            UpdateReads.ReadDeadline);
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

    #endregion
}
