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

    [Fact]
    public void A_package_changed_after_update_signing_is_refused()
    {
        var name = nameof(A_package_changed_after_update_signing_is_refused);
        var seed = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(name));
        var publicKey = Chaos.NaCl.Ed25519.PublicKeyFromSeed(seed);
        var privateKey = Chaos.NaCl.Ed25519.ExpandedPrivateKeyFromSeed(seed);
        var package = Encoding.UTF8.GetBytes("sample desktop package");
        var signature = Convert.ToBase64String(Chaos.NaCl.Ed25519.Sign(package, privateKey));
        var verifier = new NetSparkleUpdater.SignatureVerifiers.Ed25519Checker(
            NetSparkleUpdater.Enums.SecurityMode.Strict,
            Convert.ToBase64String(publicKey),
            string.Empty,
            true,
            1024);

        verifier.VerifySignature(signature, package).Should().Be(NetSparkleUpdater.Enums.ValidationResult.Valid);
        package[^1] ^= 1;
        verifier.VerifySignature(signature, package).Should().NotBe(NetSparkleUpdater.Enums.ValidationResult.Valid);
    }

    #endregion
}
