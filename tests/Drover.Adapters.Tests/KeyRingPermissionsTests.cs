namespace Drover.Adapters.Tests;

/// <summary>
/// File modes exist only on Unix, so these tests are skipped on Windows. CI and the container image run on Linux,
/// where they run.
/// </summary>
public sealed class KeyRingPermissionsTests : IDisposable
{
    #region Private Fields

    private readonly string path = Path.Combine(Path.GetTempPath(), $"drover-keyring-{Guid.NewGuid():N}");

    #endregion

    #region Public Methods

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task A_key_ring_open_to_the_group_and_others_is_tightened_at_start()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "File modes exist only on Unix.");
        Directory.CreateDirectory(path);
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var key = Path.Combine(path, "key-1.xml");
        await File.WriteAllTextAsync(key, "<key />", TestContext.Current.CancellationToken);
        File.SetUnixFileMode(
            key,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        await new KeyRingPermissionsGuard(path, NullLogger<KeyRingPermissionsGuard>.Instance)
            .StartAsync(TestContext.Current.CancellationToken);

        File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.GetUnixFileMode(key).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public async Task A_key_ring_that_does_not_exist_yet_is_left_alone()
    {
        await new KeyRingPermissionsGuard(path, NullLogger<KeyRingPermissionsGuard>.Instance)
            .StartAsync(TestContext.Current.CancellationToken);

        Directory.Exists(path).Should().BeFalse();
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void A_new_key_ring_is_created_for_its_owner_only()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "File modes exist only on Unix.");
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDroverSecretVault(path);

        File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public void Dispose()
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    #endregion
}
