namespace Drover.Server.Tests;

/// <summary>
/// A file picked on Android reaches the real upload route the way the app sends it: the picker reports no size, the
/// content stream cannot seek or tell its length, the app measures it by buffering (<c>UploadFileWithUnverifiedSize</c>)
/// and sends it with the antiforgery header. Each test makes a picture, uploads it, and reads it back from the server
/// to compare its hash, so a screenshot that "uploads" but arrives changed or empty fails too.
/// </summary>
public sealed class AndroidPickedFileUploadTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const int FileLimit = 64 * 1024;

    private ScriptedModelProvider? model;

    #endregion

    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        [$"Drover:Policy:Rules:{nameof(DeploymentPolicy.MaxFileBytes)}"] = FileLimit.ToString(CultureInfo.InvariantCulture),
    };

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));

    #endregion

    #region Uploading

    [Theory]
    [InlineData(68)]
    [InlineData(4096)]
    [InlineData(FileLimit - 1)]
    [InlineData(FileLimit)]
    public async Task A_picture_from_a_stream_with_no_length_is_stored_with_the_bytes_that_were_sent(int size)
    {
        var (client, spaceId, threadId) = await Conversation("picker");
        var api = Api(client);
        var picture = Picture(size);
        await using var content = new ContentStream(picture);
        List<long> reported = [];

        var file = await api.UploadFileWithUnverifiedSize(spaceId, threadId, "screenshot.png", content, FileLimit, new SynchronousProgress(reported.Add), Token);

        file.Size.Should().Be(size);
        file.Kind.Should().Be(FileKinds.Image);
        reported.Should().NotBeEmpty().And.BeInAscendingOrder();
        reported[^1].Should().Be(size);
        (await StoredHash(client, spaceId, file.Id)).Should().Be(Convert.ToHexStringLower(SHA256.HashData(picture)));
    }

    [Fact]
    public async Task A_picture_with_the_antiforgery_token_the_native_client_sends_is_taken_and_one_without_is_refused()
    {
        var (client, spaceId, threadId) = await Conversation("token");
        var picture = Picture(300);
        await using var withToken = new ContentStream(picture);
        await using var withoutToken = new ContentStream(picture);

        var sent = await Api(client).UploadFileWithUnverifiedSize(spaceId, threadId, "a.png", withToken, FileLimit, cancellationToken: Token);
        var refused = () => new DroverApi(NewRelay(client)).UploadFileWithUnverifiedSize(spaceId, threadId, "b.png", withoutToken, FileLimit, cancellationToken: Token);

        sent.Size.Should().Be(picture.Length);
        (await refused.Should().ThrowAsync<DroverApiException>()).Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_picture_past_the_limit_is_refused_with_the_status_and_the_sentence_the_app_names_it_by()
    {
        var (client, spaceId, threadId) = await Conversation("large");
        await using var content = new ContentStream(Picture(FileLimit + 1));

        // The client's own bound is looser than the server's, so it is the server that refuses. The app names this
        // refusal from the status, so the status and a sentence that names the limit are what is held here.
        var refused = () => Api(client).UploadFileWithUnverifiedSize(spaceId, threadId, "large.png", content, FileLimit * 4, cancellationToken: Token);

        var failure = (await refused.Should().ThrowAsync<DroverApiException>()).Which;
        failure.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        failure.Message.Should().Contain("64 KB", "the sentence names the limit the server takes");
    }

    [Fact]
    public async Task A_picture_the_client_sees_is_over_the_limit_is_refused_before_the_request_is_sent()
    {
        var (client, spaceId, threadId) = await Conversation("bound");
        await using var content = new ContentStream(Picture(FileLimit + 1));

        var refused = () => Api(client).UploadFileWithUnverifiedSize(spaceId, threadId, "large.png", content, FileLimit, cancellationToken: Token);

        (await refused.Should().ThrowAsync<DroverApiException>()).Which.Refusal.Should().Be(FileRefusals.TooLarge);
    }

    [Fact]
    public async Task A_cancelled_upload_stops_with_a_cancellation_and_not_a_failure()
    {
        var (client, spaceId, threadId) = await Conversation("cancel");
        await using var content = new ContentStream(Picture(2000));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var cancel = () => Api(client).UploadFileWithUnverifiedSize(spaceId, threadId, "c.png", content, FileLimit, cancellationToken: cancelled.Token);

        await cancel.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Private Methods

    private DroverApi Api(ApiClient client) => new(NewRelay(client)) { AntiforgeryToken = client.Cookie("XSRF-TOKEN") };

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    /// <summary>A picture of this many bytes: a PNG signature and header, then bytes that are not repeated runs.</summary>
    private static byte[] Picture(int size)
    {
        byte[] header =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, .. "IHDR"u8,
            0, 0, 0, 4, 0, 0, 0, 4, 8, 6, 0, 0, 0,
        ];
        var picture = new byte[size];
        header.AsSpan(0, Math.Min(header.Length, size)).CopyTo(picture);

        for (var index = header.Length; index < size; index++)
        {
            picture[index] = (byte)((index * 31) + (index >> 8));
        }

        return picture;
    }

    private static async Task<string> StoredHash(ApiClient client, Guid spaceId, Guid fileId)
    {
        var response = await client.Get($"{Space(spaceId)}/files/{fileId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return Convert.ToHexStringLower(SHA256.HashData(await response.Content.ReadAsByteArrayAsync(Token)));
    }

    private async Task<(ApiClient Client, Guid SpaceId, Guid ThreadId)> Conversation(string name)
    {
        var person = await SeedPerson(name);
        var client = NewClient();
        (await client.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        var connection = await Read<ModelConnectionSummary>(await client.Post(
            $"{Space(person.SpaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted")));
        await MarkAsReadingPictures(person, connection.Id);
        var bot = await Read<BotSummary>(await client.Post($"{Space(person.SpaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null)));
        var thread = await Read<ThreadSummary>(await client.Put($"{Space(person.SpaceId)}/bots/{bot.Id}/main-thread"));
        return (client, person.SpaceId, thread.Id);
    }

    // A picture has nowhere to go when the bot's model cannot read pictures, so the bot's model is marked as one that can.
    private async Task MarkAsReadingPictures((string Email, string Password, Guid UserId, Guid SpaceId) person, Guid connectionId)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(person.SpaceId, person.UserId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        db.ModelCatalog.Add(new ModelCatalogEntry
        {
            ModelConnectionId = connectionId,
            Model = "scripted",
            DisplayName = "scripted",
            Capabilities = new ModelCapabilities { Vision = true, VisionSetByOperator = true },
            LastSeenAt = Time.GetUtcNow(),
        });
        await db.SaveChangesAsync(Token);
        await work.Commit(Token);
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Token);
        response.IsSuccessStatusCode.Should().BeTrue(body);
        return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!;
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// What a <c>content://</c> picker file opens to: a stream that reads forward in short pieces, cannot seek, and
    /// does not know its length.
    /// </summary>
    private sealed class ContentStream(byte[] bytes) : Stream
    {
        #region Private Fields

        private const int Piece = 1000;

        private int position;

        #endregion

        #region Overrides

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(Math.Min(buffer.Length, Piece), bytes.Length - position);
            bytes.AsSpan(position, count).CopyTo(buffer);
            position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            cancellationToken.IsCancellationRequested
                ? ValueTask.FromCanceled<int>(cancellationToken)
                : ValueTask.FromResult(Read(buffer.Span));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        #endregion
    }

    private sealed class SynchronousProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    #endregion
}
