namespace Drover.Native.Desktop.Tests;

public sealed class SystemSecretsTests
{
    #region Public Methods

    [Fact]
    public async Task A_sign_in_written_to_this_computers_store_is_read_back_after_a_restart_and_removed_on_sign_out()
    {
        var key = $"drover.native.refresh:https://store-check-{Guid.NewGuid():N}.example.test/";
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        try
        {
            await Open().SetAsync(key, secret);

            // Nothing is shared between two openings but the operating system's store, as across a restart.
            (await Open().GetAsync(key)).Should().Be(secret);
            Open().Remove(key).Should().BeTrue();
            (await Open().GetAsync(key)).Should().BeNull();
        }
        finally
        {
            Open().Remove(key);
        }
    }

    [Fact]
    public void This_computer_has_a_store_the_app_can_use()
    {
        var open = SystemSecrets.Open;

        open.Should().NotThrow("the desktop app would otherwise keep the sign-in only until it closes");
    }

    #endregion

    #region Private Methods

    private static DesktopSecrets Open() => new(SystemSecrets.Open, NullLogger<DesktopSecrets>.Instance);

    #endregion
}
