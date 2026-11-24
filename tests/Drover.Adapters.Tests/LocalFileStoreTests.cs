namespace Drover.Adapters.Tests;

/// <summary>The local-disk file store against the file store suite, each test in a folder of its own.</summary>
public sealed class LocalFileStoreTests : FileStoreConformance, IDisposable
{
    #region Private Fields

    private readonly string root = Path.Combine(Path.GetTempPath(), $"drover-files-{Guid.NewGuid():N}");

    #endregion

    #region Tests

    [Fact]
    public async Task A_destination_failure_keeps_its_system_cause()
    {
        var space = Guid.CreateVersion7();
        var key = FileStorageKeys.For(space, Guid.CreateVersion7());
        await File.WriteAllTextAsync(root, string.Empty, TestContext.Current.CancellationToken);
        try
        {
            using var source = new MemoryStream([1]);
            var save = () => new LocalFileStore(root).Save(space, key, source, TestContext.Current.CancellationToken);

            var failure = (await save.Should().ThrowAsync<FileStorageUnavailableException>()).Which;
            failure.InnerException.Should().BeAssignableTo<IOException>();
            failure.Message.Should().NotContain(root);
        }
        finally
        {
            File.Delete(root);
        }
    }

    [Fact]
    public async Task A_file_is_kept_in_its_spaces_folder_under_its_id_and_readable_by_the_server_only()
    {
        var store = new LocalFileStore(root);
        var space = Guid.CreateVersion7();
        var file = Guid.CreateVersion7();

        await store.Save(space, FileStorageKeys.For(space, file), new MemoryStream([1]), TestContext.Current.CancellationToken);

        var path = Path.Combine(root, space.ToString("N"), file.ToString("N"));
        File.Exists(path).Should().BeTrue();
        Directory.GetFiles(Path.Combine(root, space.ToString("N"))).Should().ContainSingle("no temporary file is left");

        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.GetUnixFileMode(Path.Combine(root, space.ToString("N"))).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    #endregion

    #region Overrides

    protected override IFileStore CreateStore() => new LocalFileStore(root);

    #endregion
}
