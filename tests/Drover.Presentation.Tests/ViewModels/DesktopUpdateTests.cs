namespace Drover.Presentation.Tests.ViewModels;

/// <summary>The signed desktop update channel and its account menu controls.</summary>
public sealed class DesktopUpdateTests
{
    #region Public Methods

    [Fact]
    public async Task The_account_menu_offers_a_signed_desktop_update_and_can_turn_checks_off()
    {
        using var server = new FakeServer();
        var settings = new MemorySettings();
        var session = new SessionState(server.CreateApi());
        var theme = new ThemeModel(settings, NullLogger<ThemeModel>.Instance);
        var updates = new MemoryDesktopUpdates(new(true, true, DesktopUpdatePhase.Available, "1.2.3"));
        using var menu = new AccountMenuViewModel(
            session,
            theme,
            new ToastService(TimeProvider.System),
            new Navigator(),
            server.CreateApi(),
            NullLogger<AccountMenuViewModel>.Instance,
            updates);

        menu.ShowsUpdates.Should().BeTrue();
        menu.UpdatesLabel.Should().Be("Update 1.2.3 ready");
        menu.CanInstallUpdate.Should().BeTrue();

        menu.SetUpdateChecks(false);
        menu.UpdateChecksEnabled.Should().BeFalse();
        menu.UpdatesDetail.Should().Be("This installation does not check for updates.");

        await menu.CheckUpdates();
        await menu.InstallUpdate();
        updates.Checks.Should().Be(1);
        updates.Installs.Should().Be(1);
        menu.InstallLabel.Should().Be(OperatingSystem.IsLinux() ? "Download" : "Download and restart");
    }

    [Fact]
    public void A_failed_signature_is_shown_on_the_account_menu()
    {
        var updates = new MemoryDesktopUpdates(new(
            true,
            true,
            DesktopUpdatePhase.Failed,
            Problem: UpdatePackageTrust.SignatureProblem));
        using var menu = Menu(updates);

        menu.UpdatesDetail.Should().Be(UpdatePackageTrust.SignatureProblem);
        menu.CanInstallUpdate.Should().BeFalse();
    }

    [Fact]
    public void The_update_check_setting_is_stored_on_the_device()
    {
        var settings = new MemorySettings();
        DesktopUpdateChecks.AreEnabled(settings).Should().BeTrue();

        DesktopUpdateChecks.Store(settings, false);
        settings.Values[DesktopUpdateChecks.SettingName].Should().Be("off");
        DesktopUpdateChecks.AreEnabled(settings).Should().BeFalse();

        DesktopUpdateChecks.Store(settings, true);
        settings.Values.Should().NotContainKey(DesktopUpdateChecks.SettingName);
        DesktopUpdateChecks.AreEnabled(settings).Should().BeTrue();
    }

    [Fact]
    public void The_production_verifier_rejects_a_missing_signature_a_wrong_key_and_a_bad_key()
    {
        var (publicKey, privateKey) = Key(nameof(The_production_verifier_rejects_a_missing_signature_a_wrong_key_and_a_bad_key));
        var (otherPublic, _) = Key($"{nameof(The_production_verifier_rejects_a_missing_signature_a_wrong_key_and_a_bad_key)}-other");
        var package = Encoding.UTF8.GetBytes("sample desktop package");
        var signature = Sign(package, privateKey);
        var verifier = UpdatePackageTrust.CreateVerifier(publicKey);

        verifier.SecurityMode.Should().Be(SecurityMode.Strict);
        verifier.VerifySignature(signature, package).Should().Be(ValidationResult.Valid);
        package[^1] ^= 1;
        verifier.VerifySignature(signature, package).Should().NotBe(ValidationResult.Valid);
        package[^1] ^= 1;
        verifier.VerifySignature(string.Empty, package).Should().Be(ValidationResult.Invalid);
        UpdatePackageTrust.CreateVerifier(otherPublic).VerifySignature(signature, package).Should().Be(ValidationResult.Invalid);
        UpdatePackageTrust.CreateVerifier(string.Empty).VerifySignature(signature, package).Should().Be(ValidationResult.Invalid);
        UpdatePackageTrust.CreateVerifier("not-a-key").VerifySignature(signature, package).Should().Be(ValidationResult.Invalid);
    }

    [Fact]
    public void A_bad_feed_fails_a_manual_check_and_is_logged_for_both()
    {
        var state = new DesktopUpdateState(true, true);
        var warnings = 0;

        var manual = UpdatePackageTrust.AfterCheck(state, UpdateStatus.CouldNotDetermine, null, true, () => warnings++);
        manual.Phase.Should().Be(DesktopUpdatePhase.Failed);
        manual.Problem.Should().Be(UpdatePackageTrust.FeedProblem);

        var automatic = UpdatePackageTrust.AfterCheck(state, UpdateStatus.CouldNotDetermine, null, false, () => warnings++);
        automatic.Phase.Should().Be(DesktopUpdatePhase.Idle);
        automatic.Problem.Should().BeNull();
        warnings.Should().Be(2);
    }

    [Theory]
    [InlineData("windows", true)]
    [InlineData("mac", true)]
    [InlineData("linux", false)]
    public void A_signed_package_starts_an_installer_only_on_windows_and_macos(string hostName, bool starts)
    {
        var host = hostName switch
        {
            "windows" => UpdateHost.Windows,
            "mac" => UpdateHost.Mac,
            _ => UpdateHost.Linux,
        };
        var (publicKey, privateKey) = Key($"{nameof(A_signed_package_starts_an_installer_only_on_windows_and_macos)}{host}");
        var package = Encoding.UTF8.GetBytes($"sample desktop package{host}");
        var signature = Sign(package, privateKey);
        var link = "https://github.com/Forrest-Technologies/drover/releases/download/v1.2.3/drover-linux.deb";
        var decision = UpdatePackageTrust.Decide(
            publicKey,
            signature,
            package,
            host,
            link,
            new DesktopUpdateState(true, true, DesktopUpdatePhase.Available, "1.2.3"));

        decision.StartInstaller.Should().Be(starts);
        if (starts)
        {
            decision.State.Phase.Should().Be(DesktopUpdatePhase.Installing);
        }
        else
        {
            decision.State.Phase.Should().Be(DesktopUpdatePhase.Manual);
            decision.State.DownloadLink.Should().Be(link);
        }

        package[^1] ^= 1;
        var refused = UpdatePackageTrust.Decide(
            publicKey,
            signature,
            package,
            host,
            link,
            new DesktopUpdateState(true, true, DesktopUpdatePhase.Available, "1.2.3"));
        refused.StartInstaller.Should().BeFalse();
        refused.State.Phase.Should().Be(DesktopUpdatePhase.Failed);
        refused.State.Problem.Should().Be(UpdatePackageTrust.SignatureProblem);
    }

    [Fact]
    public void A_preview_feed_address_must_be_a_release_download()
    {
        var address = "https://github.com/Forrest-Technologies/drover/releases/download/v1.2.3-preview.1/appcast-preview-macos.xml";
        UpdatePackageTrust.RequireReleaseDownload(address).Should().Be(address);
        var act = () => UpdatePackageTrust.RequireReleaseDownload("https://example.com/appcast.xml");
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void The_download_directory_is_private_to_this_user()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drover-updates-{Guid.NewGuid():N}");
        UpdatePackageTrust.PrepareDownloadDirectory(path).Should().Be(path);
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            (mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)).Should().Be((UnixFileMode)0);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherWrite);
            var act = () => UpdatePackageTrust.PrepareDownloadDirectory(path);
            act.Should().Throw<InvalidOperationException>();
        }

        Directory.Delete(path, recursive: true);
    }

    [Fact]
    public async Task A_tampered_download_never_starts_the_installer()
    {
        var (publicKey, privateKey) = Key(nameof(A_tampered_download_never_starts_the_installer));
        var original = Encoding.UTF8.GetBytes("sample desktop package");
        var signature = Sign(original, privateKey);
        var tampered = original.ToArray();
        tampered[^1] ^= 1;
        await using var feed = await FeedHost.Open();
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var appcast = Wrap(AppCast("2.0.0", null, $"{feed.Address}package.bin", signature, os, tampered.Length));
        feed.Add("appcast.xml", Encoding.UTF8.GetBytes(appcast));
        feed.Add("appcast.xml.signature", Encoding.UTF8.GetBytes(Sign(Encoding.UTF8.GetBytes(appcast), privateKey)));
        feed.Add("package.bin", tampered);

        var directory = Path.Combine(Path.GetTempPath(), $"drover-download-{Guid.NewGuid():N}");
        using var updater = UpdatePackageTrust.CreateUpdater(
            $"{feed.Address}appcast.xml",
            publicKey,
            typeof(UpdatePackageTrust).Assembly.Location,
            directory,
            "1.0.0",
            null);
        updater.RelaunchAfterUpdate.Should().BeTrue();
        updater.TmpDownloadFilePath.Should().Be(directory);

        var result = await updater.CheckForUpdatesQuietly(ignoreSkippedVersions: true);
        result.Status.Should().Be(UpdateStatus.UpdateAvailable);
        var item = result.Updates.Single();
        var started = false;
        var act = async () =>
        {
            var path = await UpdatePackageTrust.Download(updater, item, TimeSpan.FromSeconds(20));
            var package = await File.ReadAllBytesAsync(path);
            var decision = UpdatePackageTrust.Decide(publicKey, item.DownloadSignature, package, UpdateHost.Windows, item.DownloadLink, new(true, true));
            if (decision.StartInstaller)
            {
                started = true;
            }
        };

        await act.Should().ThrowAsync<InvalidDataException>();
        started.Should().BeFalse();
        Directory.Delete(directory, true);
    }

    [Fact]
    public async Task A_preview_build_offers_a_newer_preview()
    {
        var (publicKey, privateKey) = Key(nameof(A_preview_build_offers_a_newer_preview));
        var package = Encoding.UTF8.GetBytes("preview desktop package");
        var signature = Sign(package, privateKey);
        await using var feed = await FeedHost.Open();
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var stable = AppCast("2.0.0", null, $"{feed.Address}stable.bin", signature, os, package.Length);
        var preview = AppCast("2.0.0-preview.2", "preview", $"{feed.Address}preview.bin", signature, os, package.Length);
        var appcast = $"{stable}{preview}";
        feed.Add("appcast.xml", Encoding.UTF8.GetBytes(Wrap(appcast)));
        feed.Add("appcast.xml.signature", Encoding.UTF8.GetBytes(Sign(Encoding.UTF8.GetBytes(Wrap(appcast)), privateKey)));
        feed.Add("stable.bin", package);
        feed.Add("preview.bin", package);

        var directory = Path.Combine(Path.GetTempPath(), $"drover-preview-{Guid.NewGuid():N}");
        using var updater = UpdatePackageTrust.CreateUpdater(
            $"{feed.Address}appcast.xml",
            publicKey,
            typeof(UpdatePackageTrust).Assembly.Location,
            directory,
            "1.0.0-preview.1",
            null);
        var filter = updater.AppCastHelper.AppCastFilter.Should().BeOfType<ChannelAppCastFilter>().Subject;
        filter.ChannelSearchNames.Should().Contain("preview");

        var result = await updater.CheckForUpdatesQuietly(ignoreSkippedVersions: true);
        result.Status.Should().Be(UpdateStatus.UpdateAvailable);
        result.Updates.Should().ContainSingle().Which.Version.Should().Be("2.0.0-preview.2");
        updater.RelaunchAfterUpdate.Should().BeTrue();
        Directory.Delete(directory, true);
    }

    [Theory]
    [InlineData("1.2.3", "stable")]
    [InlineData("1.2.3-preview.4", "preview")]
    public void An_installed_version_keeps_its_release_channel(string version, string channel) =>
        DesktopUpdateChannel.FromVersion(version).Should().Be(channel);

    [Theory]
    [InlineData("1.2.3", "1.2.4")]
    [InlineData("1.2.3-preview.4", "1.2.3-preview.5")]
    public void A_newer_version_is_offered(string installed, string available)
    {
        var installedVersion = NetSparkleUpdater.AppCastHandlers.SemVerLike.Parse(installed);
        var availableVersion = NetSparkleUpdater.AppCastHandlers.SemVerLike.Parse(available);

        availableVersion.CompareTo(installedVersion).Should().BePositive();
    }

    #endregion

    #region Private Methods

    private static AccountMenuViewModel Menu(MemoryDesktopUpdates updates)
    {
        using var server = new FakeServer();
        return new AccountMenuViewModel(
            new SessionState(server.CreateApi()),
            new ThemeModel(new MemorySettings(), NullLogger<ThemeModel>.Instance),
            new ToastService(TimeProvider.System),
            new Navigator(),
            server.CreateApi(),
            NullLogger<AccountMenuViewModel>.Instance,
            updates);
    }

    private static (string PublicKey, byte[] PrivateKey) Key(string name)
    {
        var seed = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(name));
        return (
            Convert.ToBase64String(Chaos.NaCl.Ed25519.PublicKeyFromSeed(seed)),
            Chaos.NaCl.Ed25519.ExpandedPrivateKeyFromSeed(seed));
    }

    private static string Sign(byte[] payload, byte[] privateKey) =>
        Convert.ToBase64String(Chaos.NaCl.Ed25519.Sign(payload, privateKey));

    private static string AppCast(string version, string? channel, string url, string signature, string os, int length)
    {
        var channelXml = channel is null ? string.Empty : $"<sparkle:channel>{channel}</sparkle:channel>";
        return $"""
            <item>
              <title>{version}</title>
              <sparkle:version>{version}</sparkle:version>
              {channelXml}
              <enclosure url="{url}" sparkle:version="{version}" sparkle:os="{os}" length="{length}" type="application/octet-stream" sparkle:edSignature="{signature}"/>
            </item>
            """;
    }

    private static string Wrap(string items) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <rss version="2.0" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">
          <channel>
            <title>Drover</title>
            {items}
          </channel>
        </rss>
        """;

    #endregion
}

/// <summary>A local release feed for one test.</summary>
file sealed class FeedHost : IAsyncDisposable
{
    #region Private Fields

    private readonly HttpListener listener = new();

    private readonly Dictionary<string, byte[]> files = [];

    private readonly CancellationTokenSource cancel = new();

    #endregion

    #region Constructors

    private FeedHost(string address) => Address = address;

    #endregion

    #region Properties

    public string Address { get; }

    #endregion

    #region Public Methods

    public static Task<FeedHost> Open()
    {
        var port = FreePort();
        var host = new FeedHost($"http://127.0.0.1:{port}/");
        host.listener.Prefixes.Add(host.Address);
        host.listener.Start();
        _ = host.Serve();
        return Task.FromResult(host);
    }

    public void Add(string name, byte[] body) => files[name] = body;

    public async ValueTask DisposeAsync()
    {
        await cancel.CancelAsync();
        listener.Stop();
        cancel.Dispose();
    }

    #endregion

    #region Private Methods

    private async Task Serve()
    {
        while (!cancel.IsCancellationRequested)
        {
            var context = await Next();
            if (context is null)
            {
                break;
            }

            var name = context.Request.Url?.AbsolutePath.TrimStart('/') ?? string.Empty;
            if (files.TryGetValue(name, out var body))
            {
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/octet-stream";
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);
            }
            else
            {
                context.Response.StatusCode = 404;
            }

            context.Response.Close();
        }
    }

    private async Task<HttpListenerContext?> Next()
    {
        try
        {
            return await listener.GetContextAsync();
        }
        catch (Exception exception) when (cancel.IsCancellationRequested || !listener.IsListening)
        {
            return exception is HttpListenerException or ObjectDisposedException or OperationCanceledException
                ? null
                : null;
        }
    }

    private static int FreePort()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    #endregion
}

