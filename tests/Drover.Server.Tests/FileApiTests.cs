namespace Drover.Server.Tests;

/// <summary>
/// Files over the API, against real PostgreSQL and the local-disk store: uploading, sending, downloading, and removing.
/// A download is authorized by the file's row, so only people who can see its conversation get it, and it is served
/// so a browser cannot run it in Drover's origin.
/// </summary>
public sealed class FileApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private readonly MemorySandboxProvider sandboxes = new();

    private ScriptedModelProvider? model;

    #endregion

    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        [PublicAddress.Variable] = "https://app.example.test",
        [Drover.Server.Files.PreviewOrigin.Variable] = "https://preview.other.test",
    };

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));
        services.AddSingleton<ISandboxProvider>(sandboxes);
        services.AddSandboxTools();
    }

    #endregion

    #region Uploading And Sending

    [Fact]
    public async Task A_native_bearer_uploads_a_picture_without_an_antiforgery_cookie_until_its_access_expires()
    {
        var (client, spaceId, threadId, botId) = await Conversation("alice");
        await GiveSandbox(client, spaceId, botId);
        using var exchange = await client.Post("/api/native/session");
        exchange.EnsureSuccessStatusCode();
        var session = await exchange.Content.ReadFromJsonAsync<JsonElement>(Token);
        using var http = NewRelay(NewClient());
        http.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        var api = new DroverApi(http);
        var png = PngHeader(3, 2);
        using var source = new MemoryStream(png);

        var stored = await api.UploadFileWithUnverifiedSize(spaceId, threadId, "dot.png", source,
            DeploymentPolicy.SelfHostedMaxFileBytes, cancellationToken: Token);
        stored.Size.Should().Be(png.Length);
        stored.Kind.Should().Be(FileKinds.Image);

        Time.Advance(TimeSpan.FromMinutes(11));
        source.Position = 0;
        var expired = () => api.UploadFileWithUnverifiedSize(spaceId, threadId, "dot.png", source,
            DeploymentPolicy.SelfHostedMaxFileBytes, cancellationToken: Token);
        var refusal = (await expired.Should().ThrowAsync<DroverApiException>()).Which;
        refusal.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_unwritable_store_reports_the_directory_at_startup_and_a_safe_upload_problem()
    {
        var root = DroverFactory.FilesPath(KeyRingPath);
        await File.WriteAllTextAsync(root, string.Empty, Token);
        try
        {
            var (client, spaceId, threadId, _) = await Conversation("alice");
            var diagnostic = $"Files directory not writable: {root}";
            Logs.Entries.Should().ContainSingle(entry =>
                entry.Level == LogLevel.Error && entry.Text.TrimEnd() == diagnostic);
            var name = $"private-{Guid.NewGuid():N}.txt";
            var words = $"private-{Guid.NewGuid():N}";
            using var http = NewRelay(client);
            var api = new DroverApi(http) { AntiforgeryToken = client.Cookie("XSRF-TOKEN") };
            using var body = new MemoryStream(Encoding.UTF8.GetBytes(words));
            var upload = () => api.UploadFile(spaceId, threadId, name, body, body.Length, cancellationToken: Token);

            var failure = (await upload.Should().ThrowAsync<DroverApiException>()).Which;
            failure.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            failure.Refusal.Should().Be("storage_unavailable");
            failure.Message.Should().Be("The server could not store this file. Contact the server administrator.");
            var warning = Logs.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning
                && entry.Text.StartsWith("The file store could not save an upload", StringComparison.Ordinal)).Which;
            warning.Text.TrimEnd().Should().MatchRegex(
                @"^The file store could not save an upload \(\w+Exception, result -?\d+\)$");
            Logs.AllText().Should().NotContain(name).And.NotContain(words);
        }
        finally
        {
            File.Delete(root);
        }
    }

    [Fact]
    public async Task A_person_uploads_a_text_file_and_sends_it_with_a_message()
    {
        var (client, spaceId, threadId, _) = await Conversation("alice");

        var upload = await client.PostContent(UploadPath(spaceId, threadId, "notes.txt"), Body("one\ntwo"));
        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync(Token));
        var file = (await upload.Content.ReadFromJsonAsync<FileResponse>(Token))!;
        var sent = await client.Post($"{Space(spaceId)}/threads/{threadId}/messages", new SendMessageRequest(null, FileIds: [file.Id]));

        file.Should().Be(new FileResponse(file.Id, "notes.txt", "text/plain", FileKinds.Text, 7, Preview: "one\ntwo"));
        upload.Headers.Location!.ToString().Should().Be($"/api/spaces/{spaceId}/files/{file.Id}");
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted, await sent.Content.ReadAsStringAsync(Token));
        var message = (await sent.Content.ReadFromJsonAsync<SendMessageResponse>(Token))!.Message;
        message.Parts.Should().ContainSingle().Which.File.Should().Be(file with { ContentType = "text/plain" });
    }

    [Fact]
    public async Task The_rules_say_what_the_bot_can_take_and_the_servers_limits()
    {
        var (client, spaceId, threadId, _) = await Conversation("alice");

        var rules = await client.Get($"{Space(spaceId)}/threads/{threadId}/attachment-rules");

        (await rules.Content.ReadFromJsonAsync<AttachmentRules>(Token)).Should().Be(
            new AttachmentRules(DeploymentPolicy.SelfHostedMaxFileBytes, DeploymentPolicy.SelfHostedMaxFilesPerMessage, ModelReadsImages: false, HasSandbox: false));
    }

    [Fact]
    public async Task A_file_the_bot_cannot_open_is_refused_with_its_reason_and_a_code()
    {
        var (client, spaceId, threadId, _) = await Conversation("alice");

        var refused = await client.PostContent(UploadPath(spaceId, threadId, "a.zip"), new ByteArrayContent([0x50, 0x4B, 0x03, 0x04, 0, 0xFF]));

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(Token);
        problem.GetProperty("title").GetString().Should().Be("Helper has no way to open this kind of file. It needs a sandbox for it.");
        problem.GetProperty("refusal").GetString().Should().Be(FileRefusals.NothingCanOpenIt);
    }

    [Fact]
    public async Task Uploading_needs_the_antiforgery_token()
    {
        var (client, spaceId, threadId, _) = await Conversation("alice");

        var refused = await client.PostWithoutToken(UploadPath(spaceId, threadId, "a.txt"));

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_name_cannot_carry_a_folder_or_control_characters()
    {
        var (client, spaceId, threadId, _) = await Conversation("alice");

        var upload = await client.PostContent(UploadPath(spaceId, threadId, "../../etc/pass\nwd.txt"), Body("x"));

        (await upload.Content.ReadFromJsonAsync<FileResponse>(Token))!.Name.Should().Be("passwd.txt");
    }

    [Fact]
    public async Task The_typed_client_uploads_with_progress_reads_the_refusal_code_and_removes_a_file()
    {
        var (client, spaceId, threadId, _) = await Conversation("alice");
        var api = new DroverApi(NewRelay(client)) { AntiforgeryToken = client.Cookie("XSRF-TOKEN") };
        var content = Encoding.UTF8.GetBytes(new string('z', 200_000));
        List<long> reported = [];

        var file = await api.UploadFile(spaceId, threadId, "z.txt", new MemoryStream(content), content.Length, new SynchronousProgress(reported.Add), Token);
        var refused = () => api.UploadFile(spaceId, threadId, "z.bin", new MemoryStream([0, 1, 2]), 3, cancellationToken: Token);

        file.Size.Should().Be(content.Length);
        reported.Should().NotBeEmpty().And.BeInAscendingOrder();
        reported[^1].Should().Be(content.Length);
        (await refused.Should().ThrowAsync<DroverApiException>()).Which.Refusal.Should().Be(FileRefusals.NothingCanOpenIt);
        api.FileAddress(spaceId, file.Id, download: true).Should().EndWith($"/api/spaces/{spaceId}/files/{file.Id}?download=true");
        await api.RemoveFile(spaceId, file.Id, Token);
        (await client.Get($"{Space(spaceId)}/files/{file.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Downloading

    [Fact]
    public async Task A_picture_is_served_with_its_own_type_and_a_policy_that_lets_it_run_nothing()
    {
        var (client, spaceId, threadId, botId) = await Conversation("alice");
        await GiveSandbox(client, spaceId, botId);
        var png = PngHeader(3, 2);
        var file = await Upload(client, spaceId, threadId, "dot.png", new ByteArrayContent(png));

        var shown = await client.Get($"{Space(spaceId)}/files/{file.Id}");
        var saved = await client.Get($"{Space(spaceId)}/files/{file.Id}?download=true");

        (file.Kind, file.Width, file.Height).Should().Be((FileKinds.Image, 3, 2));
        shown.StatusCode.Should().Be(HttpStatusCode.OK);
        (await shown.Content.ReadAsByteArrayAsync(Token)).Should().Equal(png);
        shown.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        shown.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
        shown.Content.Headers.ContentDisposition.FileNameStar.Should().Be("dot.png");
        ShouldBeSafe(shown);
        saved.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
    }

    [Theory]
    [InlineData(PreviewPictureCase.CameraJpeg)]
    [InlineData(PreviewPictureCase.PhoneScreenshot)]
    [InlineData(PreviewPictureCase.ByteHeavyPng)]
    [InlineData(PreviewPictureCase.Webp)]
    [InlineData(PreviewPictureCase.AnimatedGif)]
    public async Task Large_phone_pictures_have_bounded_decodable_previews_without_changing_the_originals(
        PreviewPictureCase pictureCase)
    {
        var (name, original, width, height) = Picture(pictureCase);
        var (client, spaceId, threadId, botId) = await Conversation($"picture-{pictureCase}");
        await GiveSandbox(client, spaceId, botId);
        var file = await Upload(client, spaceId, threadId, name, new ByteArrayContent(original));
        var sent = await client.Post(
            $"{Space(spaceId)}/threads/{threadId}/messages",
            new SendMessageRequest(null, FileIds: [file.Id]));

        (file.Kind, file.Width, file.Height).Should().Be((FileKinds.Image, width, height));
        sent.StatusCode.Should().Be(HttpStatusCode.Accepted, await sent.Content.ReadAsStringAsync(Token));
        if (pictureCase is PreviewPictureCase.CameraJpeg or PreviewPictureCase.PhoneScreenshot)
        {
            ((long)width * height).Should().BeGreaterThan(NativeFilePreview.MaxImagePixels);
        }

        if (pictureCase is PreviewPictureCase.CameraJpeg or PreviewPictureCase.ByteHeavyPng)
        {
            original.Length.Should().BeGreaterThan(2 * 1024 * 1024);
        }

        await PreviewIsBoundedAndOriginalIsIntact(client, spaceId, file, original, width, height);
        if (pictureCase is PreviewPictureCase.PhoneScreenshot)
        {
            await WarmPreviewDoesNotBroadenAccess(client, spaceId, file.Id);
        }
    }

    [Fact]
    public async Task Image_dimensions_above_the_decoder_budget_are_refused_before_decoding()
    {
        var (client, spaceId, threadId, botId) = await Conversation("oversized-image");
        await GiveSandbox(client, spaceId, botId);
        var file = await Upload(
            client,
            spaceId,
            threadId,
            "huge.png",
            new ByteArrayContent(PngHeader(20_000, 20_000)));

        var preview = await Read<NativeFilePreview>(
            await client.Get($"{Space(spaceId)}/files/{file.Id}/native-preview"));

        preview.Refusal.Should().Be(NativePreviewRefusal.TooLarge);
        preview.Image.Should().BeNull();
    }

    [Fact]
    public async Task Html_is_served_as_plain_text_and_anything_else_as_an_attachment_of_no_type()
    {
        var (client, spaceId, threadId, botId) = await Conversation("alice");
        await GiveSandbox(client, spaceId, botId);
        var page = await Upload(client, spaceId, threadId, "page.html", Body("<script>alert(document.cookie)</script>"));
        var svg = await Upload(client, spaceId, threadId, "x.svg", Body("<svg xmlns='http://www.w3.org/2000/svg' onload='alert(1)'/>"));
        var blob = await Upload(client, spaceId, threadId, "tool.exe", new ByteArrayContent([0x4D, 0x5A, 0x00, 0x01]));

        foreach (var file in new[] { page, svg })
        {
            var served = await client.Get($"{Space(spaceId)}/files/{file.Id}");
            served.Content.Headers.ContentType!.ToString().Should().Be("text/plain; charset=utf-8");
            ShouldBeSafe(served);
        }

        var other = await client.Get($"{Space(spaceId)}/files/{blob.Id}");
        other.Content.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
        other.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        ShouldBeSafe(other);
    }

    [Fact]
    public async Task The_application_policy_only_allows_the_configured_preview_frame_origin()
    {
        var response = await NewClient().Get("https://app.example.test/api/antiforgery");
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        policy.Split(';', StringSplitOptions.TrimEntries).Should()
            .ContainSingle(directive => directive.StartsWith("frame-src"))
            .Which.Should().Be("frame-src https://preview.other.test");
    }

    [Fact]
    public async Task Html_preview_is_authorized_once_sanitized_and_served_only_on_the_preview_origin()
    {
        var (owner, spaceId, threadId, botId) = await Conversation("alice");
        await GiveSandbox(owner, spaceId, botId);
        var hostile = """
            <!doctype html><base href="https://outside.example/"><meta http-equiv="refresh" content="0;url=https://outside.example">
            <h1 onclick="alert(1)" style="color: red; background-image: url(https://outside.example/a)">Report</h1>
            <script>alert(document.cookie)</script><form action="/send"><input name="secret"></form>
            <iframe src="https://outside.example"></iframe><a href="next">next</a>
            <img src="https://outside.example/a.png"><img src="/relative.png"><img src="//outside.example/a.png">
            <ul style="list-style: url(//outside.example/mark.png)"><li>item</li></ul>
            <img alt="kept" src="data:image/png;base64,iVBORw0KGgo=">
            """;
        var file = await Upload(owner, spaceId, threadId, "report.html", Body(hostile));
        var member = await SeedMember(spaceId, "bob", MembershipRole.Admin);
        var memberClient = await SignIn(member.Email, member.Password);
        var (stranger, strangerSpace, _, _) = await Conversation("eve");
        var minted = await owner.Post($"{Space(spaceId)}/files/{file.Id}/preview");
        var preview = (await minted.Content.ReadFromJsonAsync<FilePreviewResponse>(Token))!;

        (await memberClient.Post($"{Space(spaceId)}/files/{file.Id}/preview")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.Post($"{Space(strangerSpace)}/files/{file.Id}/preview")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        preview.Address.Should().StartWith("https://preview.other.test/preview/file?grant=");
        var token = new Uri(preview.Address!).Query["?grant=".Length..];
        var cleanClient = NewClient();
        (await owner.Get(preview.Address!)).StatusCode.Should().Be(HttpStatusCode.NotFound, "preview requests carrying app cookies are refused");
        (await cleanClient.PostWithoutToken(preview.Address!)).StatusCode.Should().Be(
            HttpStatusCode.NotFound, "only GET can consume a preview grant");
        var served = await cleanClient.Get(preview.Address!);
        var html = await served.Content.ReadAsStringAsync(Token);

        served.StatusCode.Should().Be(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.ToString().Should().Be("text/html; charset=utf-8");
        served.Headers.Should().NotContain(header => header.Key == "Set-Cookie");
        served.Headers.Should().NotContain(header => header.Key == "X-Frame-Options");
        served.Headers.Location.Should().BeNull("a preview response never redirects into the application origin");
        served.Headers.GetValues("Referrer-Policy").Should().Equal("no-referrer");
        served.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        served.Headers.GetValues("Cache-Control").Should().Equal("no-store");
        var policy = served.Headers.GetValues("Content-Security-Policy").Single();
        policy.Should().Contain("default-src 'none'").And.Contain("script-src 'none'").And.Contain("connect-src 'none'")
            .And.Contain("frame-src 'none'").And.Contain("object-src 'none'").And.Contain("base-uri 'none'")
            .And.Contain("form-action 'none'").And.Contain("media-src 'none'").And.Contain("frame-ancestors https://app.example.test");
        html.Should().Contain("Report").And.Contain("data:image/png;base64,iVBORw0KGgo=")
            .And.NotContain("script").And.NotContain("onclick").And.NotContain("form").And.NotContain("iframe")
            .And.NotContain("outside.example").And.NotContain("relative.png").And.NotContain("href=")
            .And.NotContain("background-image").And.NotContain("url(");
        (await cleanClient.Get(preview.Address!)).StatusCode.Should().Be(HttpStatusCode.NotFound, "a grant is single-use");
        var invalidToken = new string('a', 64);
        (await cleanClient.Get($"https://preview.other.test/preview/file?grant={invalidToken}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        Logs.AllText().Should().NotContain(token);
        Logs.AllText().Should().NotContain(invalidToken);
        (await cleanClient.Get($"https://app.example.test/preview/file?grant={token}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await cleanClient.Get("https://preview.other.test/api/antiforgery")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await cleanClient.Get("https://preview.other.test:444/api/antiforgery")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await cleanClient.Get("http://preview.other.test/api/antiforgery")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_expired_or_changed_file_preview_grant_is_refused()
    {
        var (client, spaceId, threadId, botId) = await Conversation("alice");
        await GiveSandbox(client, spaceId, botId);
        var file = await Upload(client, spaceId, threadId, "report.html", Body("<h1>Report</h1>"));
        var expiring = await MintPreview(client, spaceId, file.Id);

        Time.Advance(TimeSpan.FromMinutes(2));
        (await NewClient().Get(expiring)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        Logs.AllText().Should().NotContain(new Uri(expiring).Query["?grant=".Length..]);

        var changed = await MintPreview(client, spaceId, file.Id);
        await ChangeHash(spaceId, file.Id, new string('1', 64));
        (await NewClient().Get(changed)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_the_recipient_can_revoke_an_outstanding_preview_grant()
    {
        var (client, spaceId, threadId, botId) = await Conversation("alice");
        await GiveSandbox(client, spaceId, botId);
        var file = await Upload(client, spaceId, threadId, "report.html", Body("<h1>Report</h1>"));
        var path = $"{Space(spaceId)}/files/{file.Id}/preview";
        var member = await SeedMember(spaceId, "bob", MembershipRole.Admin);
        var otherClient = await SignIn(member.Email, member.Password);
        var issued = (await (await client.Post(path)).Content.ReadFromJsonAsync<FilePreviewResponse>(Token))!;

        (await otherClient.Post($"{path}/revoke", new FilePreviewRevokeRequest(issued.Grant!)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await NewClient().Get(issued.Address!)).StatusCode.Should().Be(HttpStatusCode.OK);

        var replacement = (await (await client.Post(path)).Content.ReadFromJsonAsync<FilePreviewResponse>(Token))!;
        (await client.Post($"{path}/revoke", new FilePreviewRevokeRequest(replacement.Grant!)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await NewClient().Get(replacement.Address!)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Removing_membership_revokes_an_outstanding_preview_grant()
    {
        var (client, spaceId, threadId, botId) = await Conversation("alice");
        await GiveSandbox(client, spaceId, botId);
        var file = await Upload(client, spaceId, threadId, "report.html", Body("<h1>Report</h1>"));
        var preview = await MintPreview(client, spaceId, file.Id);

        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand("delete from memberships where space_id = @space", connection);
        command.Parameters.AddWithValue("space", spaceId);
        (await command.ExecuteNonQueryAsync(Token)).Should().Be(1);

        (await NewClient().Get(preview)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_member_who_cannot_see_the_conversation_and_another_space_cannot_download_or_remove_the_file()
    {
        var (owner, spaceId, threadId, _) = await Conversation("alice");
        var file = await Upload(owner, spaceId, threadId, "private.txt", Body("only mine"));
        var member = await SeedMember(spaceId, "bob", MembershipRole.Admin);
        var memberClient = await SignIn(member.Email, member.Password);
        var (stranger, strangerSpace, _, _) = await Conversation("eve");

        (await memberClient.Get($"{Space(spaceId)}/files/{file.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "an admin cannot read a member's private conversation");
        (await memberClient.Delete($"{Space(spaceId)}/files/{file.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.Get($"{Space(strangerSpace)}/files/{file.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "the row is not in the stranger's space");
        (await stranger.Get($"{Space(spaceId)}/files/{file.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "the stranger is not a member");
        (await owner.Get($"{Space(spaceId)}/files/{file.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_storage_key_is_never_an_address_and_a_guessed_id_finds_nothing()
    {
        var (client, spaceId, threadId, _) = await Conversation("alice");
        var file = await Upload(client, spaceId, threadId, "a.txt", Body("x"));
        var key = FileStorageKeys.For(spaceId, file.Id);

        (await client.Get($"{Space(spaceId)}/files/{key}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.Get($"{Space(spaceId)}/files/{Uri.EscapeDataString(key)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.Get($"{Space(spaceId)}/files/{Guid.CreateVersion7()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Removing_a_file_before_sending_deletes_it_and_a_sent_file_stays()
    {
        var (client, spaceId, threadId, _) = await Conversation("alice");
        var pending = await Upload(client, spaceId, threadId, "draft.txt", Body("draft"));
        var sent = await Upload(client, spaceId, threadId, "kept.txt", Body("kept"));
        (await client.Post($"{Space(spaceId)}/threads/{threadId}/messages", new SendMessageRequest("here", FileIds: [sent.Id]))).StatusCode.Should().Be(HttpStatusCode.Accepted);

        (await client.Delete($"{Space(spaceId)}/files/{pending.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.Delete($"{Space(spaceId)}/files/{sent.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.Get($"{Space(spaceId)}/files/{pending.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.Get($"{Space(spaceId)}/files/{sent.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        Directory.GetFiles(Path.Combine(DroverFactory.FilesPath(KeyRingPath), spaceId.ToString("N"))).Should().ContainSingle();
    }

    #endregion

    #region Private Methods

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static string UploadPath(Guid spaceId, Guid threadId, string name) =>
        $"{Space(spaceId)}/threads/{threadId}/files?name={Uri.EscapeDataString(name)}";

    private static ByteArrayContent Body(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<string> MintPreview(ApiClient client, Guid spaceId, Guid fileId)
    {
        var response = await client.Post($"{Space(spaceId)}/files/{fileId}/preview");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<FilePreviewResponse>(Token))!.Address!;
    }

    private async Task ChangeHash(Guid spaceId, Guid fileId, string hash)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand("update files set sha256 = @hash where space_id = @space and id = @file", connection);
        command.Parameters.AddWithValue("hash", hash);
        command.Parameters.AddWithValue("space", spaceId);
        command.Parameters.AddWithValue("file", fileId);
        (await command.ExecuteNonQueryAsync(Token)).Should().Be(1);
    }

    /// <summary>The start of a PNG: as much as the server reads to know a picture and its size.</summary>
    private static byte[] PngHeader(int width, int height) =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, .. "IHDR"u8,
        (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
        (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
        8, 6, 0, 0, 0,
    ];

    private static byte[] Png(int width, int height, bool noise)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        Fill(bitmap, noise);
        return Encode(bitmap, SKEncodedImageFormat.Png, quality: 100);
    }

    private static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        Fill(bitmap, noise: true);
        return Encode(bitmap, SKEncodedImageFormat.Jpeg, quality: 80);
    }

    private static byte[] Webp(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        Fill(bitmap, noise: false);
        return Encode(bitmap, SKEncodedImageFormat.Webp, quality: 90);
    }

    private static void Fill(SKBitmap bitmap, bool noise)
    {
        if (!noise)
        {
            bitmap.Erase(SKColors.CornflowerBlue);
            return;
        }

        var pixels = new SKColor[bitmap.Width * bitmap.Height];
        var value = 0x12345678u;
        for (var index = 0; index < pixels.Length; index++)
        {
            value = (1664525u * value) + 1013904223u;
            pixels[index] = new((byte)value, (byte)(value >> 8), (byte)(value >> 16));
        }

        bitmap.Pixels = pixels;
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality)
            ?? throw new InvalidOperationException("Skia did not encode the test image.");
        return data.ToArray();
    }

    // A three-by-two animation. Frame one is blue and frame two is red.
    private static byte[] AnimatedGif() => Convert.FromBase64String("""
        R0lGODlhAwACAIEAAGSV7QAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQA
        CgAAACwAAAAAAwACAAAIBgABCBwYEAAh+QQBCgABACwAAAAAAwACAIH/AAAAAAAA
        AAAAAAAIBgABCBwYEAA7
        """);

    private static (string Name, byte[] Content, int Width, int Height) Picture(PreviewPictureCase pictureCase) =>
        pictureCase switch
        {
            PreviewPictureCase.CameraJpeg => ("camera.jpg", Jpeg(4000, 3000), 4000, 3000),
            PreviewPictureCase.PhoneScreenshot => ("phone-screenshot.png", Png(1344, 2992, noise: false), 1344, 2992),
            PreviewPictureCase.ByteHeavyPng => ("large-screenshot.png", Png(1080, 2400, noise: true), 1080, 2400),
            PreviewPictureCase.AnimatedGif => ("motion.gif", AnimatedGif(), 3, 2),
            PreviewPictureCase.Webp => ("camera.webp", Webp(640, 480), 640, 480),
            _ => throw new ArgumentOutOfRangeException(nameof(pictureCase), pictureCase, null),
        };

    private static async Task PreviewIsBoundedAndOriginalIsIntact(
        ApiClient client,
        Guid spaceId,
        FileResponse file,
        byte[] original,
        int originalWidth,
        int originalHeight)
    {
        var address = $"{Space(spaceId)}/files/{file.Id}";
        var previewResponse = await client.Get($"{address}/native-preview");
        var preview = await Read<NativeFilePreview>(previewResponse);

        preview.Refusal.Should().BeNull();
        preview.Image.Should().NotBeNull().And.HaveCountLessThanOrEqualTo(NativeFilePreview.MaxImageBytes);
        var bytes = preview.Image!;
        var inspector = new FileInspector();
        inspector.Append(bytes);
        var facts = inspector.Finish(string.Empty);
        facts.Kind.Should().Be(FileKind.Image);
        facts.Width.Should().NotBeNull();
        facts.Height.Should().NotBeNull();
        var previewWidth = facts.Width!.Value;
        var previewHeight = facts.Height!.Value;
        previewWidth.Should().BePositive();
        previewHeight.Should().BePositive();
        ((long)previewWidth * previewHeight)
            .Should().BeLessThanOrEqualTo(NativeFilePreview.MaxImagePixels);
        using var bitmap = SKBitmap.Decode(bytes);
        bitmap.Should().NotBeNull("the native client must be able to decode the preview response");
        ((long)bitmap!.Width * bitmap.Height).Should().BeLessThanOrEqualTo(NativeFilePreview.MaxImagePixels);
        Math.Max(bitmap.Width, bitmap.Height).Should().Be(Math.Min(480, Math.Max(originalWidth, originalHeight)));
        Math.Abs(((long)bitmap.Width * originalHeight) - ((long)bitmap.Height * originalWidth))
            .Should().BeLessThanOrEqualTo(
                Math.Max(originalWidth, originalHeight),
                "only pixel rounding may change the aspect ratio");
        if (file.Name is "phone-screenshot.png" or "camera.webp" or "motion.gif")
        {
            var pixel = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
            pixel.Red.Should().BeInRange((byte)92, (byte)108);
            pixel.Green.Should().BeInRange((byte)141, (byte)157);
            pixel.Blue.Should().BeInRange((byte)229, byte.MaxValue);
        }

        (await (await client.Get(address)).Content.ReadAsByteArrayAsync(Token)).Should().Equal(original);
    }

    private async Task WarmPreviewDoesNotBroadenAccess(ApiClient owner, Guid spaceId, Guid fileId)
    {
        var address = $"{Space(spaceId)}/files/{fileId}/native-preview";
        (await Read<NativeFilePreview>(await owner.Get(address))).Image.Should().NotBeNull("the cache is warm");
        var member = await SeedMember(spaceId, $"member-{Guid.NewGuid():N}", MembershipRole.Admin);
        var memberClient = await SignIn(member.Email, member.Password);
        var (stranger, strangerSpace, _, _) = await Conversation($"stranger-{Guid.NewGuid():N}");

        (await memberClient.Get(address)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.Get(address)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.Get($"{Space(strangerSpace)}/files/{fileId}/native-preview"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static void ShouldBeSafe(HttpResponseMessage response)
    {
        var policy = string.Join(' ', response.Headers.GetValues("Content-Security-Policy"));
        policy.Should().Contain("sandbox").And.Contain("default-src 'none'").And.NotContain("script-src");
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("Cross-Origin-Resource-Policy").Should().Equal("same-origin");
    }

    private static async Task<FileResponse> Upload(ApiClient client, Guid spaceId, Guid threadId, string name, HttpContent content)
    {
        var response = await client.PostContent(UploadPath(spaceId, threadId, name), content);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<FileResponse>(Token))!;
    }

    private async Task<(ApiClient Client, Guid SpaceId, Guid ThreadId, Guid BotId)> Conversation(string name)
    {
        var person = await SeedPerson(name);
        var client = await SignIn(person.Email, person.Password);
        var connection = await Read<ModelConnectionSummary>(await client.Post(
            $"{Space(person.SpaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted")));
        var bot = await Read<BotSummary>(await client.Post($"{Space(person.SpaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null)));
        var thread = await Read<ThreadSummary>(await client.Put($"{Space(person.SpaceId)}/bots/{bot.Id}/main-thread"));
        return (client, person.SpaceId, thread.Id, bot.Id);
    }

    /// <summary>A sandbox and the grant to read its files, as a person gives them in the bot editor.</summary>
    private static async Task GiveSandbox(ApiClient client, Guid spaceId, Guid botId)
    {
        var saved = await client.Put(
            $"{Space(spaceId)}/bots/{botId}/definition",
            new UpdateBotRequest(null, null, null, null, null, null, null, null, null, "private", new SandboxSettingsBody("off", [], null, Desktop: false), null, null));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));
        var tools = await Read<List<BotToolEntry>>(await client.Get($"{Space(spaceId)}/bots/{botId}/tools"));
        var readFile = tools.Single(tool => tool.Name == "sandbox.read_file");
        (await client.Put($"{Space(spaceId)}/bots/{botId}/tools/{readFile.ToolId}", new SetToolGrantRequest("allow"))).StatusCode.Should().Be(HttpStatusCode.OK);
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

    #region Nested Types

    public enum PreviewPictureCase
    {
        CameraJpeg,
        PhoneScreenshot,
        ByteHeavyPng,
        Webp,
        AnimatedGif,
    }

    /// <summary>Reports on the caller's thread, so a test reads every report as soon as the call returns.</summary>
    private sealed class SynchronousProgress(Action<long> report) : IProgress<long>
    {
        #region Interface Implementations

        public void Report(long value) => report(value);

        #endregion
    }

    #endregion
}
