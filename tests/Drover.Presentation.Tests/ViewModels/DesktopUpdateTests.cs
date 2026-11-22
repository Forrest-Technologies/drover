namespace Drover.Presentation.Tests.ViewModels;

/// <summary>The signed desktop update channel and its account menu controls.</summary>
public sealed class DesktopUpdateTests
{
    #region Private Fields

    private static readonly ServiceProvider clients = new ServiceCollection().AddHttpClient().BuildServiceProvider();

    #endregion

    #region Public Methods

    [Fact]
    public async Task The_account_menu_offers_a_signed_desktop_update_and_can_turn_checks_off()
    {
        using var server = new FakeServer();
        var settings = new MemorySettings();
        var session = new SessionState(server.CreateApi());
        var theme = new ThemeModel(settings, NullLogger<ThemeModel>.Instance);
        var updates = new MemoryDesktopUpdates(new(true, true, DesktopUpdatePhase.Available, "1.2.3"), settings);
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
        settings.Values[DesktopUpdateChecks.SettingName].Should().Be("off");

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
        var checkedFile = Path.Combine(Path.GetTempPath(), $"drover-checked-{hostName}.deb");
        var decision = UpdatePackageTrust.Decide(
            publicKey,
            signature,
            package,
            host,
            checkedFile,
            new DesktopUpdateState(true, true, DesktopUpdatePhase.Available, "1.2.3"));

        decision.StartInstaller.Should().Be(starts);
        if (starts)
        {
            decision.State.Phase.Should().Be(DesktopUpdatePhase.Installing);
        }
        else
        {
            decision.State.Phase.Should().Be(DesktopUpdatePhase.Manual);
            decision.State.VerifiedPackage.Should().Be(checkedFile);
            var updates = new MemoryDesktopUpdates(decision.State);
            using var menu = Menu(updates);
            menu.UpdatesDetail.Should().Be(
                $"The package signature is valid. Install the checked file at {checkedFile}. Drover does not open a privileged installer on this system.");
            menu.UpdatesDetail.Should().NotContain(link);
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
        using var client = Client();
        using var updater = UpdatePackageTrust.CreateUpdater(
            $"{feed.Address}appcast.xml",
            publicKey,
            typeof(UpdatePackageTrust).Assembly.Location,
            directory,
            "1.0.0",
            client);
        updater.RelaunchAfterUpdate.Should().BeTrue();
        updater.TmpDownloadFilePath.Should().Be(directory);

        var result = await updater.CheckForUpdatesQuietly(ignoreSkippedVersions: true);
        result.Status.Should().Be(UpdateStatus.UpdateAvailable);
        var item = result.Updates.Single();
        var started = false;
        var act = async () =>
        {
            var path = await UpdatePackageTrust.Download(
            updater,
            item,
            directory,
            TimeSpan.FromSeconds(20),
            NullLogger.Instance);
            var package = await File.ReadAllBytesAsync(path);
            var decision = UpdatePackageTrust.Decide(publicKey, item.DownloadSignature, package, UpdateHost.Windows, path, new(true, true));
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
        using var client = Client();
        using var updater = UpdatePackageTrust.CreateUpdater(
            $"{feed.Address}appcast.xml",
            publicKey,
            typeof(UpdatePackageTrust).Assembly.Location,
            directory,
            "1.0.0-preview.1",
            client);
        var filter = updater.AppCastHelper.AppCastFilter.Should().BeOfType<ReleaseChannelFilter>().Subject;
        filter.Channel.Should().Be("preview");

        var result = await updater.CheckForUpdatesQuietly(ignoreSkippedVersions: true);
        result.Status.Should().Be(UpdateStatus.UpdateAvailable);
        var item = result.Updates.Should().ContainSingle().Subject;
        item.Version.Should().Be("2.0.0-preview.2");
        var path = await UpdatePackageTrust.Download(
            updater,
            item,
            directory,
            TimeSpan.FromSeconds(20),
            NullLogger.Instance);
        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().Equal(package);
        updater.RelaunchAfterUpdate.Should().BeTrue();

        UpdateHost[] hosts = [UpdateHost.Windows, UpdateHost.Mac, UpdateHost.Linux];

        foreach (var host in hosts)
        {
            var decision = UpdatePackageTrust.Decide(
                publicKey,
                item.DownloadSignature,
                package,
                host,
                path,
                new DesktopUpdateState(true, true, DesktopUpdatePhase.Available, item.Version));
            if (UpdatePackageTrust.StartsPrivilegedInstaller(host))
            {
                decision.StartInstaller.Should().BeTrue();
                decision.State.Phase.Should().Be(DesktopUpdatePhase.Installing);
            }
            else
            {
                decision.StartInstaller.Should().BeFalse();
                decision.State.VerifiedPackage.Should().Be(path);
                decision.State.Phase.Should().Be(DesktopUpdatePhase.Manual);
            }
        }

        Directory.Delete(directory, true);
    }

    [Fact]
    public void The_newest_verified_version_is_kept_for_each_release_channel()
    {
        var settings = new MemorySettings();
        var stable = UpdatePackageTrust.FeedSettingName("stable");
        var preview = UpdatePackageTrust.FeedSettingName("preview");

        UpdatePackageTrust.AcceptFeed(settings, "stable", "1.4.0").Should().Be(new FeedVerdict(true, null));
        settings.Values[stable].Should().Be("1.4.0");

        // An older signed offer is refused and does not lower the record.
        UpdatePackageTrust.AcceptFeed(settings, "stable", "1.3.0").Accepted.Should().BeFalse();
        settings.Values[stable].Should().Be("1.4.0");

        // A preview install has its own record, so an older preview is not judged against stable.
        UpdatePackageTrust.AcceptFeed(settings, "preview", "1.3.0-preview.1").Accepted.Should().BeTrue();
        settings.Values[preview].Should().Be("1.3.0-preview.1");

        UpdatePackageTrust.AcceptFeed(settings, "stable", "1.5.0").Accepted.Should().BeTrue();
        settings.Values[stable].Should().Be("1.5.0");

        // No offer leaves the record alone.
        UpdatePackageTrust.AcceptFeed(settings, "stable", null).Accepted.Should().BeTrue();
        settings.Values[stable].Should().Be("1.5.0");
    }

    [Fact]
    public void A_version_recorded_by_an_earlier_build_is_kept_as_the_floor_for_the_channel()
    {
        var settings = new MemorySettings();
        settings.Values[UpdatePackageTrust.LegacyFeedSettingName] = "1.4.0";

        // The older offer is refused, and the old record moves to the channel record.
        UpdatePackageTrust.AcceptFeed(settings, "stable", "1.3.0").Accepted.Should().BeFalse();
        settings.Values[UpdatePackageTrust.FeedSettingName("stable")].Should().Be("1.4.0");
        settings.Values.Should().NotContainKey(UpdatePackageTrust.LegacyFeedSettingName);

        UpdatePackageTrust.AcceptFeed(settings, "stable", "1.5.0").Accepted.Should().BeTrue();
        settings.Values[UpdatePackageTrust.FeedSettingName("stable")].Should().Be("1.5.0");
    }

    [Fact]
    public void An_earlier_record_is_kept_even_when_the_check_offers_nothing()
    {
        var settings = new MemorySettings();
        settings.Values[UpdatePackageTrust.LegacyFeedSettingName] = "1.4.0";

        UpdatePackageTrust.AcceptFeed(settings, "preview", null).Accepted.Should().BeTrue();

        settings.Values[UpdatePackageTrust.FeedSettingName("preview")].Should().Be("1.4.0");
        UpdatePackageTrust.AcceptFeed(settings, "preview", "1.2.0-preview.1").Accepted.Should().BeFalse();
    }

    [Fact]
    public void A_channel_record_wins_over_an_earlier_one()
    {
        var settings = new MemorySettings();
        settings.Values[UpdatePackageTrust.LegacyFeedSettingName] = "1.4.0";
        settings.Values[UpdatePackageTrust.FeedSettingName("preview")] = "1.1.0-preview.1";

        UpdatePackageTrust.AcceptFeed(settings, "preview", "1.2.0-preview.1").Accepted.Should().BeTrue();

        settings.Values[UpdatePackageTrust.LegacyFeedSettingName].Should().Be("1.4.0", "it is left for the other channel to move");
    }

    [Fact]
    public async Task Turning_checks_off_during_a_pending_install_is_not_undone_by_the_result()
    {
        using var package = await SignedPackage(nameof(Turning_checks_off_during_a_pending_install_is_not_undone_by_the_result));
        var live = new DesktopUpdateState(true, true, DesktopUpdatePhase.Available, "2.0.0");
        List<DesktopUpdateState> shown = [];

        var state = await UpdateInstall.Run(
            new RecordingInstaller([], null),
            () =>
            {
                live = live with { ChecksEnabled = false, Phase = DesktopUpdatePhase.Idle };
                return Task.FromResult(package.Path);
            },
            package.PublicKey,
            package.Signature,
            UpdateHost.Windows,
            "2.0.0",
            () => live,
            shown.Add,
            () => { });

        state.ChecksEnabled.Should().BeFalse();
        state.Phase.Should().Be(DesktopUpdatePhase.Installing);
        shown.Should().HaveCount(2);
        shown[0].ChecksEnabled.Should().BeTrue("checks were still on when the download began");
        shown[1].ChecksEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task A_manual_result_keeps_the_checks_setting_turned_off_during_the_download()
    {
        using var package = await SignedPackage(nameof(A_manual_result_keeps_the_checks_setting_turned_off_during_the_download));
        var live = new DesktopUpdateState(true, true, DesktopUpdatePhase.Available, "2.0.0");

        var state = await UpdateInstall.Run(
            new RecordingInstaller([], null),
            () =>
            {
                live = live with { ChecksEnabled = false };
                return Task.FromResult(package.Path);
            },
            package.PublicKey,
            package.Signature,
            UpdateHost.Linux,
            "2.0.0",
            () => live,
            _ => { },
            () => { });

        state.Phase.Should().Be(DesktopUpdatePhase.Manual);
        state.ChecksEnabled.Should().BeFalse();
    }

    [Fact]
    public void A_newer_offer_whose_version_cannot_be_recorded_is_refused_and_nothing_is_offered()
    {
        var settings = new MemorySettings();
        settings.Values[UpdatePackageTrust.FeedSettingName("stable")] = "1.4.0";
        var store = new ReadOnlySettings(settings);

        var verdict = UpdatePackageTrust.AcceptFeed(store, "stable", "1.5.0");

        verdict.Accepted.Should().BeFalse("without the record, a version signed earlier could be accepted again after a restart");
        verdict.Recorded.Should().BeFalse();
        verdict.NotSaved.Should().NotBeNull();
        settings.Values[UpdatePackageTrust.FeedSettingName("stable")].Should().Be("1.4.0", "the record is as it was");

        var manual = UpdatePackageTrust.AfterUnrecordedFeed(new DesktopUpdateState(true, true, DesktopUpdatePhase.Checking), true);
        manual.Phase.Should().Be(DesktopUpdatePhase.Failed);
        manual.Problem.Should().Be(UpdatePackageTrust.UnrecordedProblem);
        manual.Version.Should().BeNull("an offer that is refused names no version, so Install has nothing to start");
        var automatic = UpdatePackageTrust.AfterUnrecordedFeed(new DesktopUpdateState(true, true, DesktopUpdatePhase.Checking), false);
        automatic.Phase.Should().Be(DesktopUpdatePhase.Idle);
        automatic.Problem.Should().BeNull();
        manual.Phase.Should().NotBe(DesktopUpdatePhase.Available);
        automatic.Phase.Should().NotBe(DesktopUpdatePhase.Available);
    }

    [Fact]
    public void A_version_already_recorded_needs_no_new_record_and_a_first_offer_with_no_record_is_refused_when_it_cannot_be_kept()
    {
        var recorded = new MemorySettings();
        recorded.Values[UpdatePackageTrust.FeedSettingName("stable")] = "1.5.0";
        var fresh = new MemorySettings();

        UpdatePackageTrust.AcceptFeed(new ReadOnlySettings(recorded), "stable", "1.5.0").Accepted.Should().BeTrue("the record already holds this version");
        UpdatePackageTrust.AcceptFeed(new ReadOnlySettings(fresh), "stable", "1.5.0").Accepted.Should().BeFalse();
        UpdatePackageTrust.AcceptFeed(new ReadOnlySettings(fresh), "stable", null).Accepted.Should().BeTrue("no offer needs no record");
    }

    [Fact]
    public async Task A_package_over_the_limit_is_refused_without_a_byte_read()
    {
        await using var declared = new CountingStream(new byte[2048], 2048);

        var refused = await UpdatePackageTrust.ReadPackage(declared, 1024);

        refused.Should().BeNull();
        declared.BytesRead.Should().Be(0, "a stream that says it is over the limit is not read");
    }

    [Fact]
    public async Task A_package_that_grows_past_what_was_measured_is_refused_after_at_most_one_more_byte()
    {
        await using var growing = new CountingStream(new byte[4096], 1024);

        var refused = await UpdatePackageTrust.ReadPackage(growing, 1024);

        refused.Should().BeNull();
        growing.BytesRead.Should().BeLessThanOrEqualTo(1025, "reading stops at the limit and the one byte that shows it was exceeded");
    }

    [Fact]
    public async Task A_stream_whose_reported_length_changes_between_reads_is_sized_by_the_length_that_was_checked()
    {
        // The first length is within the limit and every later one is far over it. A buffer sized by a second read of
        // the length would be as large as the later one, before any growth check could run.
        await using var changing = new ChangingLengthStream(new byte[4096], first: 1024, later: 1L << 40);

        var refused = await UpdatePackageTrust.ReadPackage(changing, 1024);

        refused.Should().BeNull("the content is longer than the length that was checked");
        changing.LengthReads.Should().Be(1, "the length is read once and the checked value is the one used");
        changing.BytesRead.Should().BeLessThanOrEqualTo(1025);
    }

    [Fact]
    public async Task A_package_file_over_the_limit_is_refused_and_one_at_the_limit_is_read()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drover-package-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, new byte[64], TestContext.Current.CancellationToken);

        try
        {
            (await UpdatePackageTrust.ReadPackageFile(path, 63)).Should().BeNull();
            (await UpdatePackageTrust.ReadPackageFile(path, 64))!.Length.Should().Be(64);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_package_at_the_limit_is_read_whole()
    {
        var bytes = Enumerable.Range(0, 1024).Select(index => (byte)index).ToArray();
        await using var exact = new CountingStream(bytes, 1024);

        var package = await UpdatePackageTrust.ReadPackage(exact, 1024);

        package.Should().Equal(bytes);
    }

    [Fact]
    public async Task A_download_over_the_limit_is_refused_before_the_installer_and_before_it_is_verified()
    {
        using var package = await SignedPackage(nameof(A_download_over_the_limit_is_refused_before_the_installer_and_before_it_is_verified));
        List<string> events = [];
        List<DesktopUpdatePhase> shown = [];

        var state = await UpdateInstall.Run(
            new RecordingInstaller(events, null),
            () => Task.FromResult(package.Path),
            package.PublicKey,
            package.Signature,
            UpdateHost.Windows,
            "2.0.0",
            () => new DesktopUpdateState(true, true, DesktopUpdatePhase.Available, "2.0.0"),
            value => shown.Add(value.Phase),
            () => events.Add("closed"),
            packageLimit: 8);

        events.Should().BeEmpty("nothing is handed to the installer");
        shown.Should().Equal(DesktopUpdatePhase.Downloading);
        state.Phase.Should().Be(DesktopUpdatePhase.Failed);
        state.Problem.Should().Be(UpdatePackageTrust.TooLargeProblem);
    }

    [Fact]
    public void A_stale_feed_is_shown_for_a_manual_check_and_not_for_an_automatic_one()
    {
        var state = new DesktopUpdateState(true, true, DesktopUpdatePhase.Checking);

        var manual = UpdatePackageTrust.AfterStaleFeed(state, true);
        manual.Phase.Should().Be(DesktopUpdatePhase.Failed);
        manual.Problem.Should().Be(UpdatePackageTrust.StaleProblem);
        manual.Version.Should().BeNull();

        var automatic = UpdatePackageTrust.AfterStaleFeed(state, false);
        automatic.Phase.Should().Be(DesktopUpdatePhase.Idle);
        automatic.Problem.Should().BeNull();
        automatic.Version.Should().BeNull();
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("mac")]
    public async Task A_verified_package_starts_the_installer_and_the_window_closes_after_it(string hostName)
    {
        var host = hostName is "windows" ? UpdateHost.Windows : UpdateHost.Mac;
        using var package = await SignedPackage($"{nameof(A_verified_package_starts_the_installer_and_the_window_closes_after_it)}{hostName}");
        List<string> events = [];
        var installer = new RecordingInstaller(events, null);
        List<DesktopUpdatePhase> shown = [];

        var state = await UpdateInstall.Run(
            installer,
            () => Task.FromResult(package.Path),
            package.PublicKey,
            package.Signature,
            host,
            "2.0.0",
            () => new DesktopUpdateState(true, true, DesktopUpdatePhase.Available, "2.0.0"),
            value => shown.Add(value.Phase),
            () => events.Add("closed"));

        events.Should().Equal("started", "closed");
        installer.Paths.Should().Equal(package.Path);
        shown.Should().Equal(DesktopUpdatePhase.Downloading, DesktopUpdatePhase.Installing);
        state.Phase.Should().Be(DesktopUpdatePhase.Installing);
    }

    [Fact]
    public async Task A_failed_installer_leaves_the_window_open_and_shows_the_problem()
    {
        using var package = await SignedPackage(nameof(A_failed_installer_leaves_the_window_open_and_shows_the_problem));
        List<string> events = [];
        var installer = new RecordingInstaller(events, InstallUpdateFailureReason.CouldNotBuildInstallerCommand);

        var state = await UpdateInstall.Run(
            installer,
            () => Task.FromResult(package.Path),
            package.PublicKey,
            package.Signature,
            UpdateHost.Windows,
            "2.0.0",
            () => new DesktopUpdateState(true, true, DesktopUpdatePhase.Available, "2.0.0"),
            _ => { },
            () => events.Add("closed"));

        events.Should().Equal("attempted");
        state.Phase.Should().Be(DesktopUpdatePhase.Failed);
        state.Problem.Should().Be(UpdateInstall.InstallerProblem);

        var refused = await UpdateInstall.Run(
            new RecordingInstaller(events, InstallUpdateFailureReason.InvalidSignature),
            () => Task.FromResult(package.Path),
            package.PublicKey,
            package.Signature,
            UpdateHost.Mac,
            "2.0.0",
            () => new DesktopUpdateState(true, true, DesktopUpdatePhase.Available, "2.0.0"),
            _ => { },
            () => events.Add("closed"));

        events.Should().Equal("attempted", "attempted");
        refused.Problem.Should().Be(UpdatePackageTrust.SignatureProblem);
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("mac")]
    [InlineData("linux")]
    public async Task A_package_that_fails_verification_never_reaches_the_installer(string hostName)
    {
        var host = hostName switch
        {
            "windows" => UpdateHost.Windows,
            "mac" => UpdateHost.Mac,
            _ => UpdateHost.Linux,
        };
        using var package = await SignedPackage($"{nameof(A_package_that_fails_verification_never_reaches_the_installer)}{hostName}");
        await File.AppendAllTextAsync(package.Path, "changed", TestContext.Current.CancellationToken);
        List<string> events = [];

        var state = await UpdateInstall.Run(
            new RecordingInstaller(events, null),
            () => Task.FromResult(package.Path),
            package.PublicKey,
            package.Signature,
            host,
            "2.0.0",
            () => new DesktopUpdateState(true, true, DesktopUpdatePhase.Available, "2.0.0"),
            _ => { },
            () => events.Add("closed"));

        events.Should().BeEmpty();
        state.Phase.Should().Be(DesktopUpdatePhase.Failed);
        state.Problem.Should().Be(UpdatePackageTrust.SignatureProblem);
    }

    [Fact]
    public async Task Linux_keeps_the_verified_package_and_never_starts_an_installer()
    {
        using var package = await SignedPackage(nameof(Linux_keeps_the_verified_package_and_never_starts_an_installer));
        List<string> events = [];

        var state = await UpdateInstall.Run(
            new RecordingInstaller(events, null),
            () => Task.FromResult(package.Path),
            package.PublicKey,
            package.Signature,
            UpdateHost.Linux,
            "2.0.0",
            () => new DesktopUpdateState(true, true, DesktopUpdatePhase.Available, "2.0.0"),
            _ => { },
            () => events.Add("closed"));

        events.Should().BeEmpty();
        state.Phase.Should().Be(DesktopUpdatePhase.Manual);
        state.VerifiedPackage.Should().Be(package.Path);
    }

    [Fact]
    public async Task A_failed_download_never_reaches_the_installer()
    {
        List<string> events = [];
        var act = () => UpdateInstall.Run(
            new RecordingInstaller(events, null),
            () => throw new InvalidDataException("The download failed."),
            "unused",
            "unused",
            UpdateHost.Windows,
            "2.0.0",
            () => new DesktopUpdateState(true, true, DesktopUpdatePhase.Available, "2.0.0"),
            _ => { },
            () => events.Add("closed"));

        await act.Should().ThrowAsync<InvalidDataException>();
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task The_library_installer_refuses_a_changed_package_before_anything_starts()
    {
        var (publicKey, privateKey) = Key(nameof(The_library_installer_refuses_a_changed_package_before_anything_starts));
        var original = Encoding.UTF8.GetBytes("sample desktop package");
        var directory = Path.Combine(Path.GetTempPath(), $"drover-installer-{Guid.NewGuid():N}");
        using var client = Client();
        using var updater = UpdatePackageTrust.CreateUpdater(
            "https://github.com/Forrest-Technologies/drover/releases/latest/download/appcast-linux.xml",
            publicKey,
            typeof(UpdatePackageTrust).Assembly.Location,
            directory,
            "1.0.0",
            client);
        var item = new AppCastItem { Version = "2.0.0", DownloadSignature = Sign(original, privateKey) };
        var changed = original.ToArray();
        changed[^1] ^= 1;
        var path = Path.Combine(directory, "package.bin");
        await File.WriteAllBytesAsync(path, changed, TestContext.Current.CancellationToken);
        var closed = false;

        var failure = await new SparkleInstaller(updater, item).Start(path, () => closed = true);

        failure.Should().Be(InstallUpdateFailureReason.InvalidSignature);
        closed.Should().BeFalse();
        Directory.Delete(directory, true);
    }

    [Fact]
    public async Task A_stable_build_refuses_preview_entries_and_they_do_not_move_its_version_record()
    {
        var (publicKey, privateKey) = Key(
            nameof(A_stable_build_refuses_preview_entries_and_they_do_not_move_its_version_record));
        var package = Encoding.UTF8.GetBytes("stable desktop package");
        var signature = Sign(package, privateKey);
        await using var feed = await FeedHost.Open();
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var preview = AppCast(
            "3.0.0-preview.1",
            "preview",
            $"{feed.Address}preview.bin",
            signature,
            os,
            package.Length);
        var tagged = AppCast("2.5.0", "preview", $"{feed.Address}tagged.bin", signature, os, package.Length);
        var stable = AppCast("2.0.0", null, $"{feed.Address}stable.bin", signature, os, package.Length);
        feed.AddFeed("preview-only.xml", SignedFeed(Wrap($"{preview}{tagged}"), privateKey));
        feed.AddFeed("mixed.xml", SignedFeed(Wrap($"{preview}{tagged}{stable}"), privateKey));
        var settings = new MemorySettings();
        var record = UpdatePackageTrust.FeedSettingName("stable");
        settings.Values[record] = "1.5.0";
        var directory = Path.Combine(Path.GetTempPath(), $"drover-channel-{Guid.NewGuid():N}");
        using var client = Client();

        try
        {
            using var previewOnly = StableUpdater($"{feed.Address}preview-only.xml", publicKey, directory, client);
            previewOnly.AppCastHelper.AppCastFilter.Should().BeOfType<ReleaseChannelFilter>()
                .Which.Channel.Should().Be("stable");
            var refused = await previewOnly.CheckForUpdatesQuietly(ignoreSkippedVersions: true);

            refused.Updates.Should().BeEmpty("a preview entry signed by the same key is still on another channel");
            var none = UpdatePackageTrust.Offer(refused, "stable");
            none.Should().BeNull();
            UpdatePackageTrust.AcceptFeed(settings, "stable", none?.Version).Accepted.Should().BeTrue();
            settings.Values[record].Should().Be("1.5.0", "a refused preview entry never reaches the record");

            using var mixed = StableUpdater($"{feed.Address}mixed.xml", publicKey, directory, client);
            var offered = await mixed.CheckForUpdatesQuietly(ignoreSkippedVersions: true);

            var item = UpdatePackageTrust.Offer(offered, "stable");
            item!.Version.Should().Be("2.0.0", "the newer preview entries do not hide the stable release");
            offered.Updates.Should().ContainSingle();
            UpdatePackageTrust.AcceptFeed(settings, "stable", item.Version).Accepted.Should().BeTrue();
            settings.Values[record].Should().Be("2.0.0");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Each_install_keeps_only_entries_from_its_own_release_channel()
    {
        var stable = new AppCastItem { Version = "2.0.0" };
        var preview = new AppCastItem { Version = "2.1.0-preview.1", Channel = "preview" };
        var tagged = new AppCastItem { Version = "2.2.0", Channel = "Preview" };
        var unmarked = new AppCastItem { Version = "2.3.0-preview.2" };
        var older = new AppCastItem { Version = "0.9.0" };
        var mislabeled = new AppCastItem { Version = "2.4.0-preview.3", Channel = "stable" };
        AppCastItem[] items = [stable, preview, tagged, unmarked, older, mislabeled];

        new ReleaseChannelFilter("stable").GetFilteredAppCastItems(SemVerLike.Parse("1.0.0"), items)
            .Should().Equal(stable);
        new ReleaseChannelFilter("preview").GetFilteredAppCastItems(SemVerLike.Parse("1.0.0-preview.1"), items)
            .Should().Equal(unmarked, preview);

        UpdatePackageTrust.Offer(new UpdateInfo(UpdateStatus.UpdateAvailable, [preview, tagged, stable]), "stable")
            .Should().BeSameAs(stable, "an entry from another channel is never offered, even past the filter");
        UpdatePackageTrust.Offer(new UpdateInfo(UpdateStatus.UpdateAvailable, [preview, tagged]), "stable")
            .Should().BeNull();
        UpdatePackageTrust.Offer(new UpdateInfo(UpdateStatus.UpdateNotAvailable, [stable]), "stable").Should().BeNull();
    }

    [Theory]
    [InlineData("appcast", false, 0)]
    [InlineData("appcast", false, 1)]
    [InlineData("appcast", true, 0)]
    [InlineData("appcast", true, 1)]
    [InlineData("signature", false, 0)]
    [InlineData("signature", false, 1)]
    [InlineData("signature", true, 0)]
    [InlineData("signature", true, 1)]
    public async Task A_feed_and_its_signature_are_read_up_to_the_limit_and_refused_past_it(
        string sized,
        bool streamed,
        int overLimit)
    {
        var (publicKey, privateKey) = Key(string.Join(
            '-',
            nameof(A_feed_and_its_signature_are_read_up_to_the_limit_and_refused_past_it),
            sized,
            streamed,
            overLimit));
        var package = Encoding.UTF8.GetBytes("sample desktop package");
        await using var feed = await FeedHost.Open();
        var item = AppCast(
            "2.0.0",
            null,
            $"{feed.Address}package.bin",
            Sign(package, privateKey),
            HostOs(),
            package.Length);
        var size = UpdateReads.FeedLimit + overLimit;
        var appcast = Encoding.UTF8.GetBytes(sized is "appcast" ? PaddedFeed(item, size) : Wrap(item));

        // Whitespace in a signature is ignored when it is decoded, so the padded signature is still valid: only its
        // size can refuse it.
        var signature = Sign(appcast, privateKey);
        var signatureFile = sized is "signature" ? signature.PadRight((int)size) : signature;
        feed.Add("appcast.xml", appcast, streamed);
        feed.Add("appcast.xml.signature", Encoding.UTF8.GetBytes(signatureFile), streamed);
        var directory = Path.Combine(Path.GetTempPath(), $"drover-feed-limit-{Guid.NewGuid():N}");
        using var client = Client();

        try
        {
            using var updater = StableUpdater($"{feed.Address}appcast.xml", publicKey, directory, client);

            var result = await updater.CheckForUpdatesQuietly(ignoreSkippedVersions: true);

            if (overLimit == 0)
            {
                result.Status.Should().Be(UpdateStatus.UpdateAvailable, "a read of exactly the limit is accepted");
                result.Updates.Should().ContainSingle();
            }
            else
            {
                result.Status.Should().Be(UpdateStatus.CouldNotDetermine);
                result.Updates.Should().BeEmpty();
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public async Task Preview_discovery_reads_the_release_list_up_to_the_limit_and_refuses_past_it(
        bool streamed,
        int overLimit)
    {
        const string Asset = "appcast-preview-macos.xml";
        var address = $"{UpdatePackageTrust.DownloadPrefix}v2.0.0-preview.1/{Asset}";
        var releases = $$"""
            [{"prerelease":false,"assets":[]},
             {"prerelease":true,"assets":[{"name":"{{Asset}}","browser_download_url":"{{address}}"}]}]
            """;
        var body = Encoding.UTF8.GetBytes(releases.PadRight((int)(UpdateReads.ReleaseListLimit + overLimit)));
        await using var feed = await FeedHost.Open();
        feed.Add("releases.json", body, streamed);
        using var client = Client();

        var find = () => UpdatePackageTrust.FindPreviewFeed(
            client,
            $"{feed.Address}releases.json",
            Asset,
            TimeSpan.FromSeconds(20));

        if (overLimit == 0)
        {
            (await find()).Should().Be(address, "a release list of exactly the limit is read");
        }
        else
        {
            await find.Should().ThrowAsync<InvalidDataException>().WithMessage(UpdateReads.TooLargeProblem);
        }
    }

    [Fact]
    public async Task Preview_discovery_gives_up_on_a_release_list_that_stops_arriving()
    {
        await using var feed = await FeedHost.Open();
        feed.AddStalled("releases.json", "[{"u8.ToArray());
        using var client = Client();

        var find = () => UpdatePackageTrust.FindPreviewFeed(
            client,
            $"{feed.Address}releases.json",
            "appcast-preview-macos.xml",
            TimeSpan.FromMilliseconds(300));

        await find.Should().ThrowAsync<TimeoutException>().WithMessage(UpdateReads.TooSlowProblem);
    }

    [Fact]
    public void The_updater_checks_every_file_through_the_bounded_strict_verifier()
    {
        var (publicKey, _) = Key(nameof(The_updater_checks_every_file_through_the_bounded_strict_verifier));
        var directory = Path.Combine(Path.GetTempPath(), $"drover-wiring-{Guid.NewGuid():N}");
        using var client = Client();

        try
        {
            using var updater = StableUpdater(
                "https://github.com/Forrest-Technologies/drover/releases/latest/download/appcast-linux.xml",
                publicKey,
                directory,
                client);

            var verifier = updater.SignatureVerifier.Should().BeOfType<BoundedFileVerifier>().Subject;
            verifier.Limit.Should().Be(UpdatePackageTrust.PackageLimit);
            verifier.SecurityMode.Should().Be(SecurityMode.Strict);
            var weaken = () => verifier.SecurityMode = SecurityMode.Unsafe;
            weaken.Should().Throw<NotSupportedException>();
            updater.UpdateDownloader.Should().BeOfType<BoundedPackageDownloader>()
                .Which.Limit.Should().Be(UpdatePackageTrust.PackageLimit);
            updater.AppCastDataDownloader.Should().BeOfType<BoundedFeedDownloader>()
                .Which.Limit.Should().Be(UpdateReads.FeedLimit);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task The_verifier_reads_at_most_its_limit_from_the_opened_file_and_refuses_a_link()
    {
        var (publicKey, privateKey) = Key(
            nameof(The_verifier_reads_at_most_its_limit_from_the_opened_file_and_refuses_a_link));
        var package = Encoding.UTF8.GetBytes("sample desktop package");
        var signature = Sign(package, privateKey);
        var directory = Directory.CreateTempSubdirectory("drover-verifier-").FullName;
        var path = Path.Combine(directory, "package.bin");
        await File.WriteAllBytesAsync(path, package, TestContext.Current.CancellationToken);
        var strict = UpdatePackageTrust.CreateVerifier(publicKey);

        try
        {
            new BoundedFileVerifier(strict, package.Length).VerifySignatureOfFile(signature, path)
                .Should().Be(ValidationResult.Valid, "a file of exactly the limit is read");
            new BoundedFileVerifier(strict, package.Length - 1).VerifySignatureOfFile(signature, path)
                .Should().Be(ValidationResult.Invalid, "a validly signed file over the limit is not read");

            if (!OperatingSystem.IsWindows())
            {
                var link = Path.Combine(directory, "link.bin");
                File.CreateSymbolicLink(link, path);
                new BoundedFileVerifier(strict, package.Length).VerifySignatureOfFile(signature, link)
                    .Should().Be(ValidationResult.Invalid, "a link is refused even when it points to a valid package");
                var read = () => UpdatePackageTrust.ReadPackageFile(link, package.Length);
                await read.Should().ThrowAsync<InvalidDataException>().WithMessage(UpdatePackageTrust.LinkProblem);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public async Task A_signed_package_downloads_up_to_the_limit_and_one_past_it_is_removed_unverified(
        bool streamed,
        int overLimit)
    {
        await using var served = await ServedPackage(
            string.Join(
                '-',
                nameof(A_signed_package_downloads_up_to_the_limit_and_one_past_it_is_removed_unverified),
                streamed,
                overLimit),
            streamed);
        var limit = served.Package.Length - overLimit;
        using var updater = StableUpdater(served.Feed, served.PublicKey, served.Directory, served.Client, limit);
        updater.UpdateDownloader.Should().BeOfType<BoundedPackageDownloader>().Which.Limit.Should().Be(limit);
        var offered = await Offered(updater);
        var verifier = new RecordingVerifier(updater.SignatureVerifier);
        updater.SignatureVerifier = verifier;

        var download = () => UpdatePackageTrust.Download(
            updater,
            offered,
            served.Directory,
            TimeSpan.FromSeconds(20),
            NullLogger.Instance);

        if (overLimit == 0)
        {
            var path = await download();
            (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().Equal(served.Package);
            verifier.Checks.Should().Equal(new FileCheck(served.Package.Length, Link: false, ValidationResult.Valid));
        }
        else
        {
            await download.Should().ThrowAsync<InvalidDataException>().WithMessage(UpdatePackageTrust.TooLargeProblem);
            Directory.EnumerateFiles(served.Directory, "*", SearchOption.AllDirectories)
                .Should().BeEmpty("the partial download is removed");
            verifier.Checks.Should().BeEmpty("a package over the limit is never read to check its signature");
            updater.UpdateDownloader.IsDownloading.Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("oversized")]
    [InlineData("link")]
    public async Task A_file_or_link_left_in_the_download_folder_is_never_read_or_reused(string left)
    {
        if (left is "link" && OperatingSystem.IsWindows())
        {
            // Creating a symbolic link needs a privilege a Windows test run does not have. CI runs this on Linux.
            return;
        }

        await using var served = await ServedPackage(
            string.Join('-', nameof(A_file_or_link_left_in_the_download_folder_is_never_read_or_reused), left),
            streamed: false);
        using var updater = StableUpdater(
            served.Feed,
            served.PublicKey,
            served.Directory,
            served.Client,
            served.Package.Length);
        var offered = await Offered(updater);
        var leftPath = Path.Combine(served.Directory, "package.bin");
        var outside = Path.Combine(Path.GetTempPath(), $"drover-outside-{Guid.NewGuid():N}.bin");
        var oversized = new byte[served.Package.Length + 1];
        if (left is "oversized")
        {
            await File.WriteAllBytesAsync(leftPath, oversized, TestContext.Current.CancellationToken);
        }
        else
        {
            await File.WriteAllBytesAsync(outside, oversized, TestContext.Current.CancellationToken);
            File.CreateSymbolicLink(leftPath, outside);
        }

        var verifier = new RecordingVerifier(updater.SignatureVerifier);
        updater.SignatureVerifier = verifier;

        try
        {
            var path = await UpdatePackageTrust.Download(
                updater,
                offered,
                served.Directory,
                TimeSpan.FromSeconds(20),
                NullLogger.Instance);

            path.Should().NotBe(leftPath, "each download has a new directory of its own");
            Path.GetFileName(Path.GetDirectoryName(path)).Should().StartWith(UpdatePackageTrust.AttemptPrefix);
            (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().Equal(served.Package);
            verifier.Checks.Should().Equal(
                [new FileCheck(served.Package.Length, Link: false, ValidationResult.Valid)],
                "the only file the verifier sees is the fresh download, never the one left behind");
            new FileInfo(left is "link" ? outside : leftPath).Length
                .Should().Be(oversized.Length, "what was left, and what a link points to, is not touched");
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task A_partial_download_that_cannot_be_removed_is_reported_with_why_the_download_stopped()
    {
        await using var served = await ServedPackage(
            nameof(A_partial_download_that_cannot_be_removed_is_reported_with_why_the_download_stopped),
            streamed: true,
            splitAt: 512);
        using var updater = StableUpdater(served.Feed, served.PublicKey, served.Directory, served.Client, 1024);
        var offered = await Offered(updater);
        var downloader = updater.UpdateDownloader.Should().BeOfType<BoundedPackageDownloader>().Subject;
        var completions = 0;
        downloader.DownloadFileCompleted += (_, _) => completions++;
        var blocked = BlockRemovalOnFirstProgress(updater);

        try
        {
            var download = () => UpdatePackageTrust.Download(
                updater,
                offered,
                served.Directory,
                TimeSpan.FromSeconds(20),
                NullLogger.Instance);

            var failure = (await download.Should().ThrowAsync<AggregateException>()).Which;
            failure.Message.Should().StartWith(BoundedPackageDownloader.CleanupProblem);
            failure.InnerExceptions.Should().HaveCount(2);
            failure.InnerExceptions[0].Should().BeOfType<InvalidDataException>()
                .Which.Message.Should().Be(UpdatePackageTrust.TooLargeProblem);
            IsRemovalFailure(failure.InnerExceptions[1]).Should().BeTrue("the second cause is the removal that failed");
            blocked.Path.Should().NotBeNull("part of the package was written before the limit was crossed");
            completions.Should().Be(1, "every download ends with exactly one completion");
            File.Exists(blocked.Path).Should().BeTrue("the file that could not be removed is reported, not hidden");
        }
        finally
        {
            AllowRemoval(blocked.Path);
        }
    }

    [Fact]
    public async Task A_timeout_and_a_partial_file_that_cannot_be_removed_are_reported_together()
    {
        await using var served = await ServedPackage(
            nameof(A_timeout_and_a_partial_file_that_cannot_be_removed_are_reported_together),
            streamed: true,
            stalls: true);
        using var updater = StableUpdater(served.Feed, served.PublicKey, served.Directory, served.Client);
        var offered = await Offered(updater);
        var blocked = BlockRemovalOnFirstProgress(updater);

        try
        {
            var download = () => UpdatePackageTrust.Download(
                updater,
                offered,
                served.Directory,
                TimeSpan.FromSeconds(1),
                NullLogger.Instance);

            var failure = (await download.Should().ThrowAsync<AggregateException>()).Which;
            failure.Message.Should().StartWith(UpdatePackageTrust.TimeoutProblem);
            failure.InnerExceptions[0].Should().BeOfType<TimeoutException>();
            var causes = failure.Flatten().InnerExceptions;
            causes.Should().Contain(exception => exception is OperationCanceledException, "the download was cancelled");
            causes.Should().Contain(exception => IsRemovalFailure(exception), "and its file could not be removed");
            File.Exists(blocked.Path).Should().BeTrue();
        }
        finally
        {
            AllowRemoval(blocked.Path);
        }
    }

    [Fact]
    public async Task A_download_past_its_time_limit_is_reported_only_once_it_has_stopped_and_removed_its_file()
    {
        await using var served = await ServedPackage(
            nameof(A_download_past_its_time_limit_is_reported_only_once_it_has_stopped_and_removed_its_file),
            streamed: true,
            stalls: true);
        using var updater = StableUpdater(served.Feed, served.PublicKey, served.Directory, served.Client);
        var offered = await Offered(updater);
        var downloader = updater.UpdateDownloader.Should().BeOfType<BoundedPackageDownloader>().Subject;
        var progressed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        updater.DownloadMadeProgress += (_, _, _) => progressed.TrySetResult();

        // A slow teardown. A timeout reported without waiting for it would arrive while the download still runs.
        downloader.DownloadFileCompleted += (_, _) => Thread.Sleep(300);

        var download = () => UpdatePackageTrust.Download(
            updater,
            offered,
            served.Directory,
            TimeSpan.FromSeconds(1),
            NullLogger.Instance);

        await download.Should().ThrowExactlyAsync<TimeoutException>("a clean stop adds nothing to the timeout");
        progressed.Task.IsCompleted.Should().BeTrue("part of the package was written before the time ran out");
        downloader.Stopped.IsCompleted.Should().BeTrue("the timeout is reported after the download has stopped");
        downloader.IsDownloading.Should().BeFalse();
        Directory.EnumerateFiles(served.Directory, "*", SearchOption.AllDirectories)
            .Should().BeEmpty("the partial download is removed first");
    }

    [Fact]
    public async Task A_retry_while_an_old_attempt_is_still_stopping_never_shares_or_loses_a_file()
    {
        await using var stalled = await ServedPackage(
            string.Join(
                '-',
                nameof(A_retry_while_an_old_attempt_is_still_stopping_never_shares_or_loses_a_file),
                "old"),
            streamed: true,
            stalls: true);
        await using var retry = await ServedPackage(
            string.Join(
                '-',
                nameof(A_retry_while_an_old_attempt_is_still_stopping_never_shares_or_loses_a_file),
                "new"),
            streamed: false,
            directory: stalled.Directory);
        using var old = StableUpdater(stalled.Feed, stalled.PublicKey, stalled.Directory, stalled.Client);
        var oldOffer = await Offered(old);
        var oldDownloader = old.UpdateDownloader.Should().BeOfType<BoundedPackageDownloader>().Subject;
        var oldPath = default(string);
        old.DownloadStarted += (_, path) => oldPath = path;
        var later = DateTimeOffset.UtcNow.AddDays(2);

        // The gate holds the old attempt's teardown: it cannot see its cancellation or remove its file until opened.
        using var gate = new ManualResetEventSlim(false);
        oldDownloader.DownloadProgressChanged += (_, _) => gate.Wait();

        try
        {
            var first = () => UpdatePackageTrust.Download(
                old,
                oldOffer,
                stalled.Directory,
                TimeSpan.FromSeconds(1),
                NullLogger.Instance,
                TimeSpan.FromMilliseconds(200));

            var failure = (await first.Should().ThrowAsync<AggregateException>()).Which;
            failure.Message.Should().StartWith(UpdatePackageTrust.TimeoutProblem);
            failure.InnerExceptions.Should().Contain(exception =>
                exception is TimeoutException && exception.Message == UpdatePackageTrust.TeardownProblem);
            oldDownloader.Stopped.IsCompleted.Should().BeFalse("the old attempt is still held past the deadline");
            UpdatePackageTrust.RemoveOldAttempts(stalled.Directory, null, later);
            Directory.Exists(Path.GetDirectoryName(oldPath))
                .Should().BeTrue("a check never removes the directory of an attempt that is still stopping");

            using var next = StableUpdater(retry.Feed, retry.PublicKey, stalled.Directory, retry.Client);
            var nextOffer = await Offered(next);
            var nextPath = await UpdatePackageTrust.Download(
                next,
                nextOffer,
                stalled.Directory,
                TimeSpan.FromSeconds(20),
                NullLogger.Instance);

            gate.Set();
            await oldDownloader.Stopped.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

            Path.GetDirectoryName(nextPath).Should().NotBe(Path.GetDirectoryName(oldPath));
            (await File.ReadAllBytesAsync(nextPath, TestContext.Current.CancellationToken))
                .Should().Equal(retry.Package, "the old attempt's cleanup never touched the retry's file");
            File.Exists(oldPath).Should().BeFalse("the old attempt removed only its own file");
            await WaitUntil(() =>
            {
                UpdatePackageTrust.RemoveOldAttempts(stalled.Directory, nextPath, later);
                return !Directory.Exists(Path.GetDirectoryName(oldPath));
            });
            File.Exists(nextPath).Should().BeTrue("once stopped, the old attempt is cleaned up and the kept one stays");
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public async Task Only_entries_older_than_a_day_are_removed_and_the_kept_package_stays()
    {
        var root = Directory.CreateTempSubdirectory("drover-attempts-").FullName;
        var outside = Directory.CreateTempSubdirectory("drover-outside-").FullName;
        var now = DateTimeOffset.UtcNow;
        var twoDaysAgo = now.AddDays(-2).UtcDateTime;

        try
        {
            var kept = UpdatePackageTrust.PrepareAttemptDirectory(root);
            var old = UpdatePackageTrust.PrepareAttemptDirectory(root);
            var young = UpdatePackageTrust.PrepareAttemptDirectory(root);
            old.Should().NotBe(kept);
            if (!OperatingSystem.IsWindows())
            {
                var owner = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                (File.GetUnixFileMode(old) & ~owner)
                    .Should().Be((UnixFileMode)0, "an attempt directory is private to this user");
                Directory.CreateSymbolicLink(Path.Combine(root, $"{UpdatePackageTrust.AttemptPrefix}link"), outside);
            }

            var keptPackage = Path.Combine(kept, "package.bin");
            var stray = Path.Combine(root, "package.bin");
            var outsideFile = Path.Combine(outside, "keep.bin");
            var cancellation = TestContext.Current.CancellationToken;
            await File.WriteAllBytesAsync(keptPackage, [1, 2, 3], cancellation);
            await File.WriteAllBytesAsync(Path.Combine(old, "package.bin"), [4], cancellation);
            await File.WriteAllBytesAsync(Path.Combine(young, "package.bin"), [5], cancellation);
            await File.WriteAllBytesAsync(stray, [6], cancellation);
            await File.WriteAllBytesAsync(outsideFile, [7], cancellation);
            Directory.SetLastWriteTimeUtc(kept, twoDaysAgo);
            Directory.SetLastWriteTimeUtc(old, twoDaysAgo);
            File.SetLastWriteTimeUtc(stray, twoDaysAgo);

            var problems = UpdatePackageTrust.RemoveOldAttempts(root, keptPackage, now);

            problems.Should().BeEmpty();
            Directory.EnumerateFileSystemEntries(root).Should().BeEquivalentTo(
                [kept, young],
                "an attempt a day old or newer may belong to another running copy, and the kept one is shown");
            File.Exists(keptPackage).Should().BeTrue("the verified package still shown to the person is kept");
            File.Exists(outsideFile).Should().BeTrue("a link is removed, not what it points to");
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(outside, true);
        }
    }

    [Theory]
    [InlineData(DesktopUpdatePhase.Downloading, DesktopUpdatePhase.Downloading)]
    [InlineData(DesktopUpdatePhase.Installing, DesktopUpdatePhase.Installing)]
    [InlineData(DesktopUpdatePhase.Available, DesktopUpdatePhase.Idle)]
    [InlineData(DesktopUpdatePhase.Failed, DesktopUpdatePhase.Idle)]
    public void Switching_checks_never_ends_a_download_or_install_in_progress(
        DesktopUpdatePhase before,
        DesktopUpdatePhase after)
    {
        var state = new DesktopUpdateState(true, true, before, "2.0.0");

        var off = UpdatePackageTrust.AfterChecksSwitch(state, false);
        var on = UpdatePackageTrust.AfterChecksSwitch(off, true);

        off.ChecksEnabled.Should().BeFalse();
        off.Phase.Should().Be(after);
        on.ChecksEnabled.Should().BeTrue();
        on.Phase.Should().Be(after);
    }

    [Fact]
    public async Task A_link_is_never_read_and_nothing_is_written_through_one()
    {
        if (OperatingSystem.IsWindows())
        {
            // Creating a symbolic link needs a privilege a Windows test run does not have. CI runs this on Linux.
            return;
        }

        var directory = Directory.CreateTempSubdirectory("drover-links-").FullName;
        var path = Path.Combine(directory, "package.bin");
        var link = Path.Combine(directory, "link.bin");
        byte[] content = [1, 2, 3];
        await File.WriteAllBytesAsync(path, content, TestContext.Current.CancellationToken);
        File.CreateSymbolicLink(link, path);

        try
        {
            var read = () => UpdatePackageTrust.ReadPackageFile(link, 1024);
            await read.Should().ThrowAsync<InvalidDataException>().WithMessage(UpdatePackageTrust.LinkProblem);
            UpdatePackageTrust.IsWithinLimit(link, 1024).Should().BeFalse("a link is never measured as a package");

            var throughLink = () => UpdatePackageTrust.CreatePackage(link);
            throughLink.Should().Throw<IOException>("nothing is created where a link already is");
            var overFile = () => UpdatePackageTrust.CreatePackage(path);
            overFile.Should().Throw<IOException>("an existing file is never replaced");

            (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken))
                .Should().Equal(content, "the file a link points to is never written");
            (await UpdatePackageTrust.ReadPackageFile(path, 1024)).Should().Equal(content);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task A_download_that_stops_after_the_deadline_has_its_late_cleanup_failure_logged()
    {
        await using var served = await ServedPackage(
            nameof(A_download_that_stops_after_the_deadline_has_its_late_cleanup_failure_logged),
            streamed: true,
            stalls: true);
        using var updater = StableUpdater(served.Feed, served.PublicKey, served.Directory, served.Client);
        var offered = await Offered(updater);
        var downloader = updater.UpdateDownloader.Should().BeOfType<BoundedPackageDownloader>().Subject;
        var log = new RecordingLogger();

        // The gate holds teardown past the deadline. Once it opens, the file is made impossible to remove, so the late
        // outcome is a cleanup failure.
        using var gate = new ManualResetEventSlim(false);
        downloader.DownloadProgressChanged += (_, _) => gate.Wait();
        var blocked = BlockRemovalOnFirstProgress(updater);

        try
        {
            var download = () => UpdatePackageTrust.Download(
                updater,
                offered,
                served.Directory,
                TimeSpan.FromSeconds(1),
                log,
                TimeSpan.FromMilliseconds(200));

            var failure = (await download.Should().ThrowAsync<AggregateException>()).Which;
            failure.InnerExceptions.Should().Contain(exception =>
                exception is TimeoutException && exception.Message == UpdatePackageTrust.TeardownProblem);
            log.Entries.Should().BeEmpty("the download has not stopped yet");

            gate.Set();
            await WaitUntil(() => !log.Entries.IsEmpty);

            var entry = log.Entries.Should().ContainSingle().Subject;
            entry.Level.Should().Be(LogLevel.Warning);
            entry.Message.Should().Contain(Path.GetDirectoryName(blocked.Path), "the attempt is named");
            entry.Exception.Should().BeOfType<AggregateException>()
                .Which.InnerExceptions.Should().Contain(exception => IsRemovalFailure(exception));
        }
        finally
        {
            gate.Set();
            await downloader.Stopped.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            AllowRemoval(blocked.Path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_check_that_resumes_after_a_newer_check_began_changes_nothing(bool olderFails)
    {
        using var probe = new FlowProbe(new MemorySettings(), new DesktopUpdateState(true, true));

        var older = probe.Flow.Check(reportFailure: true);
        probe.State.Phase.Should().Be(DesktopUpdatePhase.Checking);
        probe.Flow.SetChecks(false);
        probe.Flow.SetChecks(true);
        probe.Feeds.Should().HaveCount(2, "turning checks back on starts a newer check");
        probe.Feeds[1].SetException(new InvalidDataException("Sample: no feed."));
        await WaitUntil(() => probe.State.Phase is DesktopUpdatePhase.Idle);
        var settled = probe.State;
        var changes = probe.Changes;

        if (olderFails)
        {
            probe.Feeds[0].SetException(new InvalidDataException("Sample: the older feed failed."));
        }
        else
        {
            probe.Feeds[0].SetResult("https://example.invalid/appcast.xml");
        }

        await older;

        probe.Builds.Should().Be(0, "a superseded check never replaces the updater");
        probe.Sweeps.Should().Be(0, "a superseded check never clears old downloads");
        probe.Changes.Should().Be(changes, "a superseded check never changes what is shown");
        probe.State.Should().Be(settled);
    }

    [Fact]
    public async Task A_check_that_resumes_after_the_setting_changed_leaves_a_download_alone()
    {
        using var probe = new FlowProbe(new MemorySettings(), new DesktopUpdateState(true, true));

        var older = probe.Flow.Check(reportFailure: true);
        probe.Flow.SetChecks(false);
        probe.State = probe.State with { ChecksEnabled = true, Phase = DesktopUpdatePhase.Downloading };
        var changes = probe.Changes;

        probe.Feeds[0].SetResult("https://example.invalid/appcast.xml");
        await older;

        probe.Builds.Should().Be(0);
        probe.Sweeps.Should().Be(0);
        probe.Changes.Should().Be(changes);
        probe.State.Phase.Should().Be(DesktopUpdatePhase.Downloading);
    }

    [Theory]
    [InlineData(DesktopUpdatePhase.Downloading, DesktopUpdatePhase.Downloading, null)]
    [InlineData(DesktopUpdatePhase.Installing, DesktopUpdatePhase.Installing, null)]
    [InlineData(DesktopUpdatePhase.Available, DesktopUpdatePhase.Failed, UpdatePackageTrust.SettingProblem)]
    public void A_setting_that_cannot_be_saved_never_ends_a_download_or_install(
        DesktopUpdatePhase before,
        DesktopUpdatePhase after,
        string? problem)
    {
        var settings = new ReadOnlySettings(new MemorySettings());
        using var probe = new FlowProbe(settings, new DesktopUpdateState(true, true, before, "2.0.0"));

        probe.Flow.SetChecks(false);

        probe.State.Phase.Should().Be(after);
        probe.State.Problem.Should().Be(problem);
        probe.State.ChecksEnabled.Should().BeTrue("the setting did not change");
        probe.Feeds.Should().BeEmpty();
    }

    [Fact]
    public async Task A_cancelled_download_stops_and_its_partial_file_is_removed()
    {
        await using var feed = await FeedHost.Open();
        feed.AddStalled("package.bin", new byte[512]);
        var path = Path.Combine(Path.GetTempPath(), $"drover-partial-{Guid.NewGuid():N}.bin");
        using var client = Client();
        using var downloader = new BoundedPackageDownloader(client, 4096);
        var progressed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = default(global::System.ComponentModel.AsyncCompletedEventArgs);
        downloader.DownloadProgressChanged += (_, _) => progressed.TrySetResult();
        downloader.DownloadFileCompleted += (_, args) => completed = args;

        var running = downloader.DownloadFile(new Uri($"{feed.Address}package.bin"), path);
        await progressed.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        File.Exists(path).Should().BeTrue("the partial file is on disk while the download runs");
        downloader.IsDownloading.Should().BeTrue();

        downloader.CancelDownload();
        await running;

        completed.Should().NotBeNull();
        completed!.Cancelled.Should().BeTrue();
        File.Exists(path).Should().BeFalse();
        downloader.IsDownloading.Should().BeFalse();
        downloader.Stopped.IsCompleted.Should().BeTrue();
    }

    [Theory]
    [InlineData("1.2.3", "stable")]
    [InlineData("1.2.3-preview.4", "preview")]
    public void An_installed_version_keeps_its_release_channel(string version, string channel) =>
        DesktopUpdateChannel.FromVersion(version).Should().Be(channel);

    [Theory]
    [InlineData("1.2.0", "1.1.0", true)]
    [InlineData("1.2.0-preview.2", "1.2.0-preview.1", true)]
    [InlineData("1.2.0-preview.1", "1.2.0", false)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData(null, "1.0.0", false)]
    public void An_older_signed_feed_is_refused_after_a_newer_one_was_verified(string? seen, string offered, bool older) =>
        UpdatePackageTrust.IsOlderThanSeen(seen, offered).Should().Be(older);

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

    private static HttpClient Client() => clients.GetRequiredService<IHttpClientFactory>().CreateClient();

    private static SparkleUpdater StableUpdater(
        string feed,
        string publicKey,
        string directory,
        HttpClient client,
        long packageLimit = UpdatePackageTrust.PackageLimit) => UpdatePackageTrust.CreateUpdater(
            feed,
            publicKey,
            typeof(UpdatePackageTrust).Assembly.Location,
            directory,
            "1.0.0",
            client,
            packageLimit);

    private static string HostOs() => OperatingSystem.IsWindows()
        ? "windows"
        : OperatingSystem.IsMacOS()
            ? "macos"
            : "linux";

    /// <summary>
    /// A signed feed with <paramref name="item"/>, padded with a comment to exactly <paramref name="size"/> bytes.
    /// </summary>
    private static string PaddedFeed(string item, long size)
    {
        var bare = Encoding.UTF8.GetByteCount(Wrap($"{item}<!---->"));
        return Wrap($"{item}<!--{new string('x', (int)(size - bare))}-->");
    }

    /// <summary>A signed 4 KiB package and its signed feed, served from a fresh host into a fresh directory.</summary>
    private static async Task<ServedPackage> ServedPackage(
        string name,
        bool streamed,
        int splitAt = 0,
        bool stalls = false,
        string? directory = null)
    {
        var (publicKey, privateKey) = Key(name);
        var package = Enumerable.Range(0, 4096).Select(index => (byte)index).ToArray();
        var feed = await FeedHost.Open();
        var item = AppCast(
            "2.0.0",
            null,
            $"{feed.Address}package.bin",
            Sign(package, privateKey),
            HostOs(),
            package.Length);
        feed.AddFeed("appcast.xml", SignedFeed(Wrap(item), privateKey));
        feed.Add("package.bin", stalls ? package[..512] : package, streamed, splitAt, stalls);
        var folder = directory ?? Path.Combine(Path.GetTempPath(), $"drover-served-{Guid.NewGuid():N}");
        return new(feed, Client(), $"{feed.Address}appcast.xml", publicKey, package, folder);
    }

    private static async Task<AppCastItem> Offered(SparkleUpdater updater)
    {
        var result = await updater.CheckForUpdatesQuietly(ignoreSkippedVersions: true);
        return result.Updates.Should().ContainSingle().Subject;
    }

    /// <summary>
    /// Makes the file a download writes impossible to remove once part of it is written: on Windows by marking it read
    /// only, and elsewhere by taking write permission off its directory. Returns the file's path once it is blocked.
    /// </summary>
    private static BlockedFile BlockRemovalOnFirstProgress(SparkleUpdater updater)
    {
        var blocked = new BlockedFile();
        var started = default(string);
        updater.DownloadStarted += (_, path) => started = path;
        updater.DownloadMadeProgress += (_, _, _) =>
        {
            if (blocked.Path is not null || started is null)
            {
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(started, FileAttributes.ReadOnly);
            }
            else
            {
                File.SetUnixFileMode(Path.GetDirectoryName(started)!, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }

            blocked.Path = started;
        };

        return blocked;
    }

    private static void AllowRemoval(string? path)
    {
        if (path is null)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(path, FileAttributes.Normal);
            return;
        }

        File.SetUnixFileMode(
            Path.GetDirectoryName(path)!,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>Checks <paramref name="condition"/> every 50 ms until it holds, for at most 10 seconds.</summary>
    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (!condition())
        {
            DateTimeOffset.UtcNow.Should().BeBefore(deadline, "it should hold soon after the download stops");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    private static bool IsRemovalFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;

    private static (byte[] Body, string Signature) SignedFeed(string appcast, byte[] privateKey)
    {
        var body = Encoding.UTF8.GetBytes(appcast);
        return (body, Sign(body, privateKey));
    }

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

    private static async Task<SignedFile> SignedPackage(string name)
    {
        var (publicKey, privateKey) = Key(name);
        var package = Encoding.UTF8.GetBytes($"sample desktop package {name}");
        var path = Path.Combine(Path.GetTempPath(), $"drover-signed-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, package, TestContext.Current.CancellationToken);
        return new SignedFile(path, publicKey, Sign(package, privateKey));
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
internal sealed class FeedHost : IAsyncDisposable
{
    #region Private Fields

    private readonly HttpListener listener = new();

    private readonly Dictionary<string, FeedReply> files = [];

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

    /// <summary>
    /// Serves <paramref name="body"/>, with its length declared, or in chunks with no length when streamed. A body
    /// split at <paramref name="splitAt"/> is sent in two parts a moment apart. A stalled body sends nothing more.
    /// </summary>
    public void Add(string name, byte[] body, bool streamed = false, int splitAt = 0, bool stalls = false) =>
        files[name] = new(body, streamed, stalls, splitAt);

    /// <summary>Serves a signed feed and its detached signature.</summary>
    public void AddFeed(string name, (byte[] Body, string Signature) feed)
    {
        Add(name, feed.Body);
        Add($"{name}.signature", Encoding.UTF8.GetBytes(feed.Signature));
    }

    /// <summary>Sends <paramref name="prefix"/> in chunks and then nothing more until the host closes.</summary>
    public void AddStalled(string name, byte[] prefix) => Add(name, prefix, streamed: true, stalls: true);

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
            var next = await Next();
            if (next.Context is null)
            {
                break;
            }

            // Each reply runs on its own, so a stalled reply does not hold up the next request.
            _ = Respond(next.Context);
        }
    }

    /// <summary>Sends one reply. Returns why it stopped early, if it did.</summary>
    private async Task<Exception?> Respond(HttpListenerContext context)
    {
        var name = context.Request.Url?.AbsolutePath.TrimStart('/') ?? string.Empty;
        if (!files.TryGetValue(name, out var reply))
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
            return null;
        }

        try
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/octet-stream";
            if (reply.Streamed)
            {
                context.Response.SendChunked = true;
            }
            else
            {
                context.Response.ContentLength64 = reply.Body.Length;
            }

            var split = reply.SplitAt > 0 ? reply.SplitAt : reply.Body.Length;
            await context.Response.OutputStream.WriteAsync(reply.Body.AsMemory(0, split), cancel.Token);
            await context.Response.OutputStream.FlushAsync(cancel.Token);
            if (split < reply.Body.Length)
            {
                await Task.Delay(300, cancel.Token);
                await context.Response.OutputStream.WriteAsync(reply.Body.AsMemory(split), cancel.Token);
                await context.Response.OutputStream.FlushAsync(cancel.Token);
            }

            if (reply.Stalls)
            {
                await Task.Delay(Timeout.Infinite, cancel.Token);
            }

            context.Response.Close();
            return null;
        }
        catch (Exception exception) when (exception is HttpListenerException or IOException or ObjectDisposedException
            or OperationCanceledException)
        {
            // The client stopped reading or the host closed, which is what a refused or stalled reply is for.
            context.Response.Abort();
            return exception;
        }
    }

    /// <summary>The next request, or why the listener stopped when it was closed.</summary>
    private async Task<(HttpListenerContext? Context, Exception? Stopped)> Next()
    {
        try
        {
            return (await listener.GetContextAsync(), null);
        }
        catch (Exception exception) when ((cancel.IsCancellationRequested || !listener.IsListening)
            && exception is HttpListenerException or ObjectDisposedException or OperationCanceledException)
        {
            return (null, exception);
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



/// <summary>One file the test feed serves, and how it is sent.</summary>
internal sealed record FeedReply(byte[] Body, bool Streamed, bool Stalls, int SplitAt);


/// <summary>A signed package and its feed on a test host, with a client and download folder, removed after.</summary>
internal sealed record ServedPackage(
    FeedHost Host,
    HttpClient Client,
    string Feed,
    string PublicKey,
    byte[] Package,
    string Directory) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Host.DisposeAsync();
        if (global::System.IO.Directory.Exists(Directory))
        {
            global::System.IO.Directory.Delete(Directory, true);
        }
    }
}


/// <summary>The file a download wrote, once it has been made impossible to remove.</summary>
internal sealed class BlockedFile
{
    public string? Path { get; set; }
}


/// <summary>An update check flow with feeds the test completes, and counts of what the flow did.</summary>
file sealed class FlowProbe : IDisposable
{
    #region Constructors

    public FlowProbe(IDeviceSettings settings, DesktopUpdateState state)
    {
        State = state;
        Flow = new(
            settings,
            "stable",
            NextFeed,
            BuildUpdater,
            () => Sweeps++,
            () => State,
            Change,
            NullLogger.Instance);
    }

    #endregion

    #region Properties

    public UpdateCheckFlow Flow { get; }

    public DesktopUpdateState State { get; set; }

    public List<TaskCompletionSource<string>> Feeds { get; } = [];

    public int Changes { get; private set; }

    public int Builds { get; private set; }

    public int Sweeps { get; private set; }

    #endregion

    #region Interface Implementations

    public void Dispose() => Flow.Dispose();

    #endregion

    #region Private Methods

    private Task<string> NextFeed()
    {
        var feed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Feeds.Add(feed);
        return feed.Task;
    }

    private SparkleUpdater BuildUpdater(string feed)
    {
        Builds++;
        throw new InvalidOperationException($"Sample: this test builds no updater for {feed}.");
    }

    private void Change(DesktopUpdateState state)
    {
        State = state;
        Changes++;
    }

    #endregion
}


/// <summary>A logger that keeps every entry written to it.</summary>
file sealed class RecordingLogger : ILogger
{
    #region Properties

    public ConcurrentBag<LogEntry> Entries { get; } = [];

    #endregion

    #region Interface Implementations

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(new(logLevel, formatter(state, exception), exception));

    #endregion
}


/// <summary>One entry a <see cref="RecordingLogger"/> kept.</summary>
file sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);


/// <summary>What the verifier saw at one file check.</summary>
file readonly record struct FileCheck(long Size, bool Link, ValidationResult Result);


/// <summary>A signature checker that records the file it was asked to check each time, and what it answered.</summary>
file sealed class RecordingVerifier(NetSparkleUpdater.Interfaces.ISignatureVerifier inner)
    : NetSparkleUpdater.Interfaces.ISignatureVerifier
{
    #region Properties

    public List<FileCheck> Checks { get; } = [];

    public SecurityMode SecurityMode
    {
        get => inner.SecurityMode;
        set => inner.SecurityMode = value;
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
        var file = new FileInfo(binaryPath);
        var result = inner.VerifySignatureOfFile(signature, binaryPath);
        Checks.Add(new(file.Length, file.LinkTarget is not null, result));
        return result;
    }

    #endregion
}


/// <summary>A package on disk with the key and signature that verify it.</summary>
internal sealed record SignedFile(string Path, string PublicKey, string Signature) : IDisposable
{
    public void Dispose() => File.Delete(Path);
}


/// <summary>An installer that records what the update flow asked of it.</summary>
file sealed class RecordingInstaller(List<string> events, InstallUpdateFailureReason? failure) : IUpdateInstaller
{
    #region Properties

    public List<string> Paths { get; } = [];

    #endregion

    #region Interface Implementations

    public Task<InstallUpdateFailureReason?> Start(string path, Action closeApplication)
    {
        Paths.Add(path);
        if (failure is not null)
        {
            events.Add("attempted");
            return Task.FromResult(failure);
        }

        events.Add("started");
        closeApplication();
        return Task.FromResult<InstallUpdateFailureReason?>(null);
    }

    #endregion
}


/// <summary>Settings that can be read but not changed.</summary>
file sealed class ReadOnlySettings(IDeviceSettings inner) : IDeviceSettings
{
    public string? Read(string name) => inner.Read(name);

    public void Write(string name, string value) => throw new InvalidOperationException("Sample: storage is read only.");

    public void Remove(string name) => throw new InvalidOperationException("Sample: storage is read only.");
}


/// <summary>A stream that counts what is read from it and says how long it is, which may be less than it holds.</summary>
file sealed class CountingStream(byte[] content, long declared) : Stream
{
    #region Private Fields

    private int position;

    #endregion

    #region Properties

    public int BytesRead { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => declared;

    public override long Position
    {
        get => position;
        set => throw new NotSupportedException();
    }

    #endregion

    #region Interface Implementations

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = Math.Min(count, content.Length - position);
        Array.Copy(content, position, buffer, offset, read);
        position += read;
        BytesRead += read;
        return read;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = Math.Min(buffer.Length, content.Length - position);
        content.AsMemory(position, read).CopyTo(buffer);
        position += read;
        BytesRead += read;
        return ValueTask.FromResult(read);
    }

    public override int ReadByte()
    {
        if (position >= content.Length)
        {
            return -1;
        }

        BytesRead++;
        return content[position++];
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    #endregion
}


/// <summary>A stream whose reported length is one value the first time it is asked and another after that.</summary>
file sealed class ChangingLengthStream(byte[] content, long first, long later) : Stream
{
    #region Private Fields

    private int position;

    #endregion

    #region Properties

    public int LengthReads { get; private set; }

    public int BytesRead { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => ++LengthReads == 1 ? first : later;

    public override long Position
    {
        get => position;
        set => throw new NotSupportedException();
    }

    #endregion

    #region Interface Implementations

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = Math.Min(count, content.Length - position);
        Array.Copy(content, position, buffer, offset, read);
        position += read;
        BytesRead += read;
        return read;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = Math.Min(buffer.Length, content.Length - position);
        content.AsMemory(position, read).CopyTo(buffer);
        position += read;
        BytesRead += read;
        return ValueTask.FromResult(read);
    }

    public override int ReadByte()
    {
        if (position >= content.Length)
        {
            return -1;
        }

        BytesRead++;
        return content[position++];
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    #endregion
}

