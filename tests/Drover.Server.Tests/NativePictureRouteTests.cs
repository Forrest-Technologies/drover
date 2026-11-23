namespace Drover.Server.Tests;

/// <summary>Real preview routes under concurrent file-store reads and request cancellation.</summary>
public sealed class NativePictureRouteTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private readonly MemorySandboxProvider sandboxes = new();

    private GatedFileStore store = null!;

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        store = new(new LocalFileStore(DroverFactory.FilesPath(KeyRingPath)));
        services.RemoveAll<IFileStore>();
        services.AddSingleton<IFileStore>(store);
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));
        services.AddSingleton<ISandboxProvider>(sandboxes);
        services.AddSandboxTools();
    }

    #endregion

    #region Tests

    [Fact]
    public async Task Six_simultaneous_cold_picture_routes_wait_and_all_return_images()
    {
        var (client, spaceId, threadId, botId) = await Conversation("six-pictures");
        await GiveSandbox(client, spaceId, botId);
        var pictures = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => Upload(
            client,
            spaceId,
            threadId,
            $"picture-{index}.png")));
        using var gate = store.HoldReads(expected: NativeFilePreview.MaxPicturesPerUser);
        var requests = pictures.Select(file => client.Get(PreviewAddress(spaceId, file.Id))).ToArray();

        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), Token);
            requests.Should().OnlyContain(request => !request.IsCompleted,
                "all six route requests remain outstanding while the member's read slots are occupied");
        }
        finally
        {
            gate.Release();
            await Task.WhenAll(requests);
        }

        foreach (var request in requests)
        {
            using var response = await request;
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var preview = await response.Content.ReadFromJsonAsync<NativeFilePreview>(Token);
            preview!.Refusal.Should().BeNull();
            preview.Image.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task A_picture_route_timeout_returns_a_retryable_refusal()
    {
        var (client, spaceId, threadId, botId) = await Conversation("picture-timeout");
        await GiveSandbox(client, spaceId, botId);
        var picture = await Upload(client, spaceId, threadId, "waiting.png");
        using var gate = store.HoldReads(expected: 1);
        var pending = client.Get(PreviewAddress(spaceId, picture.Id));
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), Token);

        try
        {
            using var response = await pending.WaitAsync(TimeSpan.FromSeconds(15), Token);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var preview = await response.Content.ReadFromJsonAsync<NativeFilePreview>(Token);
            preview!.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
            preview.Image.Should().BeNull();
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task A_cancelled_picture_route_honors_request_aborted()
    {
        var (client, spaceId, threadId, botId) = await Conversation("picture-aborted");
        await GiveSandbox(client, spaceId, botId);
        var picture = await Upload(client, spaceId, threadId, "cancelled.png");
        using var gate = store.HoldReads(expected: 1);
        using var relay = NewRelay(client);
        using var cancelled = new CancellationTokenSource();
        var pending = relay.GetAsync(PreviewAddress(spaceId, picture.Id), cancelled.Token);
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), Token);

        try
        {
            await cancelled.CancelAsync();
            var read = async () => await pending;
            await read.Should().ThrowAsync<OperationCanceledException>();
            await gate.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(5), Token);
        }
        finally
        {
            gate.Release();
        }
    }

    #endregion

    #region Private Methods

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static string UploadPath(Guid spaceId, Guid threadId, string name) =>
        $"{Space(spaceId)}/threads/{threadId}/files?name={Uri.EscapeDataString(name)}";

    private static string PreviewAddress(Guid spaceId, Guid fileId) =>
        $"{Space(spaceId)}/files/{fileId}/native-preview";

    private static async Task<FileResponse> Upload(ApiClient client, Guid spaceId, Guid threadId, string name)
    {
        var bytes = Convert.FromBase64String("""
            iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAFUlEQVR4nGNg2HCBNDSq
            YVTD8NUAAFV1gBC4fs/iAAAAAElFTkSuQmCC
            """);
        using var response = await client.PostContent(
            UploadPath(spaceId, threadId, name),
            new ByteArrayContent(bytes));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<FileResponse>(Token))!;
    }

    private async Task<(ApiClient Client, Guid SpaceId, Guid ThreadId, Guid BotId)> Conversation(string name)
    {
        var person = await SeedPerson(name);
        var client = await SignIn(person.Email, person.Password);
        var connection = await Read<ModelConnectionSummary>(await client.Post(
            $"{Space(person.SpaceId)}/model-connections",
            new CreateModelConnectionRequest(
                ScriptedModelProvider.ConnectionKind,
                "Scripted",
                null,
                null,
                "scripted")));
        var bot = await Read<BotSummary>(await client.Post(
            $"{Space(person.SpaceId)}/bots",
            new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null)));
        var thread = await Read<ThreadSummary>(
            await client.Put($"{Space(person.SpaceId)}/bots/{bot.Id}/main-thread"));
        return (client, person.SpaceId, thread.Id, bot.Id);
    }

    private static async Task GiveSandbox(ApiClient client, Guid spaceId, Guid botId)
    {
        var saved = await client.Put(
            $"{Space(spaceId)}/bots/{botId}/definition",
            new UpdateBotRequest(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "private",
                new SandboxSettingsBody("off", [], null, Desktop: false),
                null,
                null));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));
        var tools = await Read<List<BotToolEntry>>(await client.Get($"{Space(spaceId)}/bots/{botId}/tools"));
        var readFile = tools.Single(tool => tool.Name == "sandbox.read_file");
        var granted = await client.Put(
            $"{Space(spaceId)}/bots/{botId}/tools/{readFile.ToolId}",
            new SetToolGrantRequest("allow"));
        granted.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Token);
        response.IsSuccessStatusCode.Should().BeTrue(body);
        return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!;
    }

    #endregion

    #region Test Doubles

    private sealed class GatedFileStore(IFileStore inner) : IFileStore
    {
        #region Private Fields

        private ReadGate? next;

        #endregion

        #region Public Methods

        public ReadGate HoldReads(int expected)
        {
            var gate = new ReadGate(expected);
            Volatile.Write(ref next, gate);
            return gate;
        }

        #endregion

        #region Interface Implementations

        public Task Save(
            Guid spaceId,
            string storageKey,
            Stream content,
            CancellationToken cancellationToken) =>
            inner.Save(spaceId, storageKey, content, cancellationToken);

        public async Task<Stream?> Open(
            Guid spaceId,
            string storageKey,
            CancellationToken cancellationToken)
        {
            var content = await inner.Open(spaceId, storageKey, cancellationToken);
            var gate = Volatile.Read(ref next);
            return content is null || gate is null ? content : new GatedReadStream(content, gate);
        }

        public Task Delete(Guid spaceId, string storageKey, CancellationToken cancellationToken) =>
            inner.Delete(spaceId, storageKey, cancellationToken);

        #endregion
    }

    private sealed class ReadGate(int expected) : IDisposable
    {
        #region Private Fields

        private readonly TaskCompletionSource cancellationObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int readers;

        #endregion

        #region Properties

        public Task CancellationObserved => cancellationObserved.Task;

        public Task Entered => entered.Task;

        #endregion

        #region Interface Implementations

        public void Dispose() => Release();

        #endregion

        #region Public Methods

        public void Release() => released.TrySetResult();

        public async Task Wait(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref readers) >= expected)
            {
                entered.TrySetResult();
            }

            try
            {
                await released.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.TrySetResult();
                throw;
            }
        }

        #endregion
    }

    private sealed class GatedReadStream(Stream inner, ReadGate gate) : Stream
    {
        #region Properties

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        #endregion

        #region Overrides

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await gate.Wait(cancellationToken);
            return await inner.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        #endregion
    }

    #endregion
}
