namespace Drover.Adapters.Conformance.Files;

/// <summary>
/// What every <see cref="IFileStore"/> must do. An adapter inherits this class and supplies a fresh store. The
/// store is the one tenancy path in Drover that is not a table row, so most of this suite is about keys: a key of
/// another space, and anything that is not a key at all, is refused before anything is read, written, or removed.
/// </summary>
public abstract class FileStoreConformance
{
    #region Properties

    /// <summary>Keys that are not keys: ways out of a folder, other separators, other spellings of an id.</summary>
    public static TheoryData<string> NotKeys
    {
        get
        {
            var space = Space.ToString("N");
            var file = Guid.CreateVersion7().ToString("N");
            return
            [
                string.Empty,
                "..",
                $"../{file}",
                $"{space}/../{file}",
                $"{space}/..",
                $"{space}/./{file}",
                $"/{space}/{file}",
                $"/etc/passwd",
                $"{space}\\{file}",
                $"{space}/{file}/x",
                $"{space}/{file}.png",
                $"{space}/{file}\0",
                $"{space}//{file}",
                $"{space}/{file.ToUpperInvariant()}",
                $"{space.ToUpperInvariant()}/{file}",
                $"{space}/%2e%2e%2f{file[6..]}",
                $"{Space:D}/{file}",
                $"{space}/{Guid.CreateVersion7():D}",
                $"{space}",
                $"{space}/",
                $"C:\\{space}\\{file}",
            ];
        }
    }

    private static Guid Space { get; } = Guid.CreateVersion7();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #endregion

    #region Adapter Hooks

    protected abstract IFileStore CreateStore();

    #endregion

    #region Tests

    [Fact]
    public async Task What_was_saved_opens_byte_for_byte()
    {
        var store = CreateStore();
        var key = NewKey(Space);
        var content = RandomBytes(300_000);

        await store.Save(Space, key, new MemoryStream(content), Token);

        (await Read(store, Space, key)).Should().Equal(content);
    }

    [Fact]
    public async Task Opening_a_key_with_nothing_under_it_is_null()
    {
        var store = CreateStore();

        (await store.Open(Space, NewKey(Space), Token)).Should().BeNull();
    }

    [Fact]
    public async Task Saving_again_replaces_the_content()
    {
        var store = CreateStore();
        var key = NewKey(Space);
        await store.Save(Space, key, new MemoryStream(RandomBytes(1000)), Token);
        var second = RandomBytes(10);

        await store.Save(Space, key, new MemoryStream(second), Token);

        (await Read(store, Space, key)).Should().Equal(second);
    }

    [Fact]
    public async Task Delete_removes_the_content_and_deleting_again_is_not_an_error()
    {
        var store = CreateStore();
        var key = NewKey(Space);
        await store.Save(Space, key, new MemoryStream(RandomBytes(10)), Token);

        await store.Delete(Space, key, Token);
        await store.Delete(Space, key, Token);

        (await store.Open(Space, key, Token)).Should().BeNull();
    }

    [Fact]
    public async Task A_save_that_fails_part_way_leaves_nothing_behind()
    {
        var store = CreateStore();
        var key = NewKey(Space);

        var save = () => store.Save(Space, key, new FailingStream(RandomBytes(100_000)), Token);

        var failure = (await save.Should().ThrowAsync<IOException>()).Which;
        failure.Should().NotBeOfType<FileStorageUnavailableException>();
        (await store.Open(Space, key, Token)).Should().BeNull();
    }

    [Fact]
    public async Task Another_spaces_key_is_refused_even_when_its_file_exists()
    {
        var store = CreateStore();
        var other = Guid.CreateVersion7();
        var theirs = NewKey(other);
        await store.Save(other, theirs, new MemoryStream(RandomBytes(10)), Token);

        var open = () => store.Open(Space, theirs, Token);
        var save = () => store.Save(Space, theirs, new MemoryStream(RandomBytes(10)), Token);
        var delete = () => store.Delete(Space, theirs, Token);

        await open.Should().ThrowAsync<ArgumentException>();
        await save.Should().ThrowAsync<ArgumentException>();
        await delete.Should().ThrowAsync<ArgumentException>();
        (await Read(store, other, theirs)).Should().HaveCount(10, "a refused call changes nothing");
    }

    [Fact]
    public async Task The_same_file_id_in_two_spaces_names_two_files()
    {
        var store = CreateStore();
        var other = Guid.CreateVersion7();
        var fileId = Guid.CreateVersion7();
        await store.Save(Space, FileStorageKeys.For(Space, fileId), new MemoryStream([1, 2, 3]), Token);

        await store.Save(other, FileStorageKeys.For(other, fileId), new MemoryStream([9]), Token);

        (await Read(store, Space, FileStorageKeys.For(Space, fileId))).Should().Equal([1, 2, 3]);
    }

    [Theory]
    [MemberData(nameof(NotKeys))]
    public async Task Something_that_is_not_a_key_is_refused_by_every_operation(string notKey)
    {
        var store = CreateStore();

        var open = () => store.Open(Space, notKey, Token);
        var save = () => store.Save(Space, notKey, new MemoryStream([1]), Token);
        var delete = () => store.Delete(Space, notKey, Token);

        await open.Should().ThrowAsync<ArgumentException>();
        await save.Should().ThrowAsync<ArgumentException>();
        await delete.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Saving_reads_content_asynchronously_without_calling_synchronous_Read()
    {
        var store = CreateStore();
        var key = NewKey(Space);
        var content = RandomBytes(128 * 1024);

        await store.Save(Space, key, new AsyncOnlyStream(content), Token);

        (await Read(store, Space, key)).Should().Equal(content);
    }

    #endregion

    #region Private Methods

    private static string NewKey(Guid spaceId) => FileStorageKeys.For(spaceId, Guid.CreateVersion7());

    private static byte[] RandomBytes(int length) => RandomNumberGenerator.GetBytes(length);

    private static async Task<byte[]> Read(IFileStore store, Guid spaceId, string key)
    {
        await using var content = await store.Open(spaceId, key, Token) ?? throw new FileNotFoundException(key);
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, Token);
        return copy.ToArray();
    }

    #endregion

    #region Nested Types

    /// <summary>Gives some of its bytes, then fails, as an upload cut off by the network does.</summary>
    private sealed class FailingStream(byte[] content) : MemoryStream(content)
    {
        #region Overrides

        public override int Read(byte[] buffer, int offset, int count) =>
            Position > 0 ? throw new IOException("The upload was cut off.") : base.Read(buffer, offset, Math.Min(count, 1000));

        public override int Read(Span<byte> buffer) =>
            Position > 0 ? throw new IOException("The upload was cut off.") : base.Read(buffer[..Math.Min(buffer.Length, 1000)]);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));

        #endregion
    }

    /// <summary>Fails on any synchronous read so <see cref="IFileStore.Save"/> must read asynchronously.</summary>
    private sealed class AsyncOnlyStream(byte[] content) : Stream
    {
        #region Private Fields

        private int position;

        #endregion

        #region Properties

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        #endregion

        #region Overrides

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(Span<byte> buffer) => throw new NotSupportedException();

        public override int ReadByte() => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = content.Length - position;
            var toCopy = Math.Min(remaining, buffer.Length);
            content.AsMemory(position, toCopy).CopyTo(buffer);
            position += toCopy;
            return ValueTask.FromResult(toCopy);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        #endregion
    }

    #endregion
}
