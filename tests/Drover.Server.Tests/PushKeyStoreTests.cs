namespace Drover.Server.Tests;

/// <summary>
/// The server's Web Push key pair on disk: made once, the same for every server process that starts at once, and
/// never made again behind the operator's back when the stored one cannot be read. Each test has its own folder.
/// </summary>
public sealed class PushKeyStoreTests : IDisposable
{
    #region Private Fields

    private const string Subject = "https://drover.example.test";

    private readonly string keyRing = Path.Combine(Path.GetTempPath(), $"drover-push-keys-{Guid.NewGuid():N}");

    #endregion

    #region Public Methods

    [Fact]
    public async Task Servers_starting_at_once_all_end_up_with_the_one_pair_that_was_stored()
    {
        Directory.CreateDirectory(keyRing);
        var protection = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(keyRing, "ring")));

        var opened = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Store(protection).Open(Subject))));

        opened.Select(availability => availability.PublicKey).Distinct().Should().ContainSingle().Which.Should().NotBeNull();
        Directory.GetFiles(keyRing).Select(Path.GetFileName).Should().Equal(PushKeyStore.FileName);
        Store(protection).Open(Subject).PublicKey.Should().Be(opened[0].PublicKey, "a later start reads the same pair");
    }

    [Fact]
    public void A_pair_the_key_ring_cannot_read_turns_push_off_with_the_fix_and_is_never_replaced()
    {
        Directory.CreateDirectory(keyRing);
        var made = Store(Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(keyRing, "first-ring")))).Open(Subject);
        var file = Path.Combine(keyRing, PushKeyStore.FileName);
        var stored = File.ReadAllText(file);

        var reopened = Store(Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(keyRing, "another-ring")))).Open(Subject);

        made.Keys.Should().NotBeNull();
        reopened.Keys.Should().BeNull();
        reopened.Problem.Should().Contain("cannot be read with the current key ring").And.Contain("turn notifications on again");
        File.ReadAllText(file).Should().Be(stored, "a new pair would cut off every device without a word");
    }

    [Fact]
    public void A_key_ring_folder_the_server_cannot_write_turns_push_off_with_the_fix()
    {
        // A file where the folder should be: no process, root included, can write beneath it.
        File.WriteAllText(keyRing, "not a folder");
        var protection = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"drover-ring-{Guid.NewGuid():N}")));

        var opened = Store(protection).Open(Subject);

        opened.Keys.Should().BeNull();
        opened.Problem.Should().Contain("cannot write its Web Push keys").And.Contain("writable");
    }

    [Fact]
    public void The_stored_pair_does_not_hold_either_key_in_the_clear()
    {
        Directory.CreateDirectory(keyRing);
        var keys = Store(Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(keyRing, "ring")))).Open(Subject).Keys!;

        var stored = File.ReadAllText(Path.Combine(keyRing, PushKeyStore.FileName));

        stored.Should().NotContain(keys.PrivateKey).And.NotContain(keys.PublicKey);
        keys.ToString().Should().NotContain(keys.PrivateKey);
    }

    public void Dispose()
    {
        if (Directory.Exists(keyRing))
        {
            Directory.Delete(keyRing, recursive: true);
        }
        else if (File.Exists(keyRing))
        {
            File.Delete(keyRing);
        }
    }

    #endregion

    #region Private Methods

    private PushKeyStore Store(Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection) =>
        new(keyRing, protection, NullLogger<PushKeyStore>.Instance);

    #endregion
}
