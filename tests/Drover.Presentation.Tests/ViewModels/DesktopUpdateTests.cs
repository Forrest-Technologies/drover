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
        using var updater = UpdatePackageTrust.CreateUpdater(
            $"{feed.Address}appcast.xml",
            publicKey,
            typeof(UpdatePackageTrust).Assembly.Location,
            directory,
            "1.0.0");
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
        using var updater = UpdatePackageTrust.CreateUpdater(
            $"{feed.Address}appcast.xml",
            publicKey,
            typeof(UpdatePackageTrust).Assembly.Location,
            directory,
            "1.0.0-preview.1");
        var filter = updater.AppCastHelper.AppCastFilter.Should().BeOfType<ChannelAppCastFilter>().Subject;
        filter.ChannelSearchNames.Should().Contain("preview");

        var result = await updater.CheckForUpdatesQuietly(ignoreSkippedVersions: true);
        result.Status.Should().Be(UpdateStatus.UpdateAvailable);
        var item = result.Updates.Should().ContainSingle().Subject;
        item.Version.Should().Be("2.0.0-preview.2");
        var path = await UpdatePackageTrust.Download(updater, item, TimeSpan.FromSeconds(20));
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
        using var updater = UpdatePackageTrust.CreateUpdater(
            "https://github.com/Forrest-Technologies/drover/releases/latest/download/appcast-linux.xml",
            publicKey,
            typeof(UpdatePackageTrust).Assembly.Location,
            directory,
            "1.0.0");
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

