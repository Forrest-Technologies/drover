namespace Drover.Server.Tests;

/// <summary>
/// Shared project files over HTTP against real PostgreSQL and local-disk storage: roles, content inspection, size and
/// space quota limits, safe download headers, missing and cross-space ids, and cleanup on project deletion.
/// </summary>
public sealed class ProjectFileApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        [$"Drover:Policy:Rules:{nameof(DeploymentPolicy.MaxFileBytes)}"] = "2048",
        [$"Drover:Policy:Rules:{nameof(DeploymentPolicy.MaxSpaceFileMegabytes)}"] = "1",
    };

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => new ScriptedModelProvider(new ScriptedModel()));

    #endregion

    #region Roles And Lifecycle

    [Fact]
    public async Task A_project_upload_releases_its_quota_when_storage_becomes_unavailable()
    {
        var owner = await SeedPerson("owner");
        var client = await SignIn(owner);
        var project = await CreateProject(client, owner.SpaceId, "Handbook");
        var folder = Path.Combine(DroverFactory.FilesPath(KeyRingPath), owner.SpaceId.ToString("N"));
        await File.WriteAllTextAsync(folder, string.Empty, Token);

        using var response = await client.PostContent(UploadPath(owner.SpaceId, project.Id, "sample.txt"), TextBody("words"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        problem.GetProperty("refusal").GetString().Should().Be(FileRefusals.StorageUnavailable);
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(owner.SpaceId, owner.UserId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        (await db.FileByteReservations.AnyAsync(Token)).Should().BeFalse();
    }

    [Fact]
    public async Task Owners_and_admins_upload_and_remove_project_files_while_members_can_list_and_download_only()
    {
        var owner = await SeedPerson("owner");
        var admin = await SeedMember(owner.SpaceId, "admin", MembershipRole.Admin);
        var member = await SeedMember(owner.SpaceId, "member", MembershipRole.Member);
        var asOwner = await SignIn(owner);
        var asAdmin = await SignIn(admin.Email, admin.Password);
        var asMember = await SignIn(member.Email, member.Password);
        var project = await CreateProject(asOwner, owner.SpaceId, "Handbook");

        // Owner uploads a text file (including HTML content served safely as plain text) and an admin uploads a binary file.
        var uploadedText = await UploadProjectFile(asOwner, owner.SpaceId, project.Id, "page.html", "<script>alert(1)</script>");
        uploadedText.ProjectId.Should().Be(project.Id);
        uploadedText.File.Name.Should().Be("page.html");
        uploadedText.File.Kind.Should().Be(FileKinds.Text);

        var uploadedBin = await UploadProjectFileBytes(asAdmin, owner.SpaceId, project.Id, "archive.zip", [0x50, 0x4B, 0x03, 0x04, 0, 0xFF]);
        uploadedBin.ProjectId.Should().Be(project.Id);
        uploadedBin.File.Kind.Should().Be(FileKinds.Other);

        // Members cannot upload or remove project files, nor delete a project file via the conversation pending-file endpoint.
        var memberUpload = await asMember.PostContent(UploadPath(owner.SpaceId, project.Id, "nope.txt"), TextBody("no"));
        var memberDelete = await asMember.Delete($"{Space(owner.SpaceId)}/projects/{project.Id}/files/{uploadedText.File.Id}");
        var bypassDelete = await asOwner.Delete($"{Space(owner.SpaceId)}/files/{uploadedText.File.Id}");

        memberUpload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        memberDelete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        bypassDelete.StatusCode.Should().Be(HttpStatusCode.NotFound, "pending-file delete cannot remove a project file");

        // Any space member can list and download shared project files, with safe headers.
        var listed = await GetFor<List<ProjectFileResponse>>(asMember, $"{Space(owner.SpaceId)}/projects/{project.Id}/files");
        listed.Select(entry => entry.File.Id).Should().Equal(uploadedBin.File.Id, uploadedText.File.Id);

        var htmlDownload = await asMember.Get($"{Space(owner.SpaceId)}/projects/{project.Id}/files/{uploadedText.File.Id}");
        htmlDownload.StatusCode.Should().Be(HttpStatusCode.OK);
        htmlDownload.Content.Headers.ContentType!.ToString().Should().Be("text/plain; charset=utf-8");
        htmlDownload.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
        ShouldBeSafe(htmlDownload);

        var binDownload = await asMember.Get($"{Space(owner.SpaceId)}/projects/{project.Id}/files/{uploadedBin.File.Id}");
        binDownload.StatusCode.Should().Be(HttpStatusCode.OK);
        binDownload.Content.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
        binDownload.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        ShouldBeSafe(binDownload);

        var forcedDownload = await asMember.Get($"{Space(owner.SpaceId)}/projects/{project.Id}/files/{uploadedText.File.Id}?download=true");
        forcedDownload.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");

        // Admin deletes one file; subsequent delete or download returns 404.
        (await asAdmin.Delete($"{Space(owner.SpaceId)}/projects/{project.Id}/files/{uploadedText.File.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await asAdmin.Delete($"{Space(owner.SpaceId)}/projects/{project.Id}/files/{uploadedText.File.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asMember.Get($"{Space(owner.SpaceId)}/projects/{project.Id}/files/{uploadedText.File.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Project_file_native_previews_follow_project_access_and_removal()
    {
        var owner = await SeedPerson("preview-owner");
        var member = await SeedMember(owner.SpaceId, "preview-member", MembershipRole.Member);
        var stranger = await SeedPerson("preview-stranger");
        var asOwner = await SignIn(owner);
        var asMember = await SignIn(member.Email, member.Password);
        var asStranger = await SignIn(stranger);
        var project = await CreateProject(asOwner, owner.SpaceId, "Notes");
        var uploaded = await UploadProjectFile(asOwner, owner.SpaceId, project.Id, "notes.txt", "shared notes");
        var previewPath = $"{Space(owner.SpaceId)}/files/{uploaded.File.Id}/native-preview";

        var preview = await asMember.Get(previewPath);

        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        preview.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await preview.Content.ReadFromJsonAsync<NativeFilePreview>(Token))!.Markdown.Should().Be("shared notes");
        (await asStranger.Get(previewPath)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asStranger.Get($"{Space(stranger.SpaceId)}/files/{uploaded.File.Id}/native-preview"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await asOwner.Delete($"{Space(owner.SpaceId)}/projects/{project.Id}/files/{uploaded.File.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await asMember.Get(previewPath)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Limits And Cross-Space Isolation

    [Fact]
    public async Task Project_file_uploads_enforce_file_size_and_space_quota_limits()
    {
        var owner = await SeedPerson("quota");
        var client = await SignIn(owner);
        var project = await CreateProject(client, owner.SpaceId, "Limits");

        var tooBig = await client.PostContent(UploadPath(owner.SpaceId, project.Id, "big.txt"), TextBody(new string('x', 3000)));
        tooBig.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        var tooBigProblem = await tooBig.Content.ReadFromJsonAsync<JsonElement>(Token);
        tooBigProblem.GetProperty("title").GetString().Should().Be("This file is 2.9 KB. This server takes files up to 2 KB.");

        var tooBigStreamed = await client.PostContent(
            UploadPath(owner.SpaceId, project.Id, "streamed.txt"),
            new UnmeasuredContent(Encoding.UTF8.GetBytes(new string('y', 3000))));
        tooBigStreamed.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        var streamedProblem = await tooBigStreamed.Content.ReadFromJsonAsync<JsonElement>(Token);
        streamedProblem.GetProperty("refusal").GetString().Should().Be(FileRefusals.TooLarge);

        // Fill the 1 MB space quota (512 * 2048 bytes).
        var fullChunk = new string('q', 2048);
        for (var index = 0; index < 512; index++)
        {
            await UploadProjectFile(client, owner.SpaceId, project.Id, $"{index}.txt", fullChunk);
        }

        var spaceFull = await client.PostContent(UploadPath(owner.SpaceId, project.Id, "overflow.txt"), TextBody(fullChunk));
        spaceFull.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var spaceFullProblem = await spaceFull.Content.ReadFromJsonAsync<JsonElement>(Token);
        spaceFullProblem.GetProperty("refusal").GetString().Should().Be(FileRefusals.SpaceFull);

        // Deleting one file returns its quota so another file of the same size fits.
        var files = await GetFor<List<ProjectFileResponse>>(client, $"{Space(owner.SpaceId)}/projects/{project.Id}/files");
        (await client.Delete($"{Space(owner.SpaceId)}/projects/{project.Id}/files/{files[0].File.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await UploadProjectFile(client, owner.SpaceId, project.Id, "replacement.txt", fullChunk);
    }

    [Fact]
    public async Task Cross_space_missing_and_mismatched_project_ids_return_not_found()
    {
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        var asAlice = await SignIn(alice);
        var asBob = await SignIn(bob);
        var projectOne = await CreateProject(asAlice, alice.SpaceId, "One");
        var projectTwo = await CreateProject(asAlice, alice.SpaceId, "Two");
        var aliceFile = await UploadProjectFile(asAlice, alice.SpaceId, projectOne.Id, "secret.txt", "alice only");
        var missingId = Guid.CreateVersion7();

        // Cross-space access returns 404.
        (await asBob.Get($"{Space(alice.SpaceId)}/projects/{projectOne.Id}/files")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asBob.PostContent(UploadPath(bob.SpaceId, projectOne.Id, "x.txt"), TextBody("x"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asBob.Get($"{Space(bob.SpaceId)}/projects/{projectOne.Id}/files/{aliceFile.File.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asBob.Delete($"{Space(bob.SpaceId)}/projects/{projectOne.Id}/files/{aliceFile.File.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Missing or wrong project in the same space returns 404.
        (await asAlice.Get($"{Space(alice.SpaceId)}/projects/{missingId}/files")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asAlice.PostContent(UploadPath(alice.SpaceId, missingId, "x.txt"), TextBody("x"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asAlice.Get($"{Space(alice.SpaceId)}/projects/{projectTwo.Id}/files/{aliceFile.File.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asAlice.Delete($"{Space(alice.SpaceId)}/projects/{projectTwo.Id}/files/{aliceFile.File.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Project Removal Cleanup

    [Fact]
    public async Task Removing_a_project_deletes_its_files_and_frees_disk_and_quota_while_keeping_conversations()
    {
        var owner = await SeedPerson("owner");
        var client = await SignIn(owner);
        var bot = await CreateBot(client, owner.SpaceId);
        var project = await CreateProject(client, owner.SpaceId, "Temporary");
        var projectFile = await UploadProjectFile(client, owner.SpaceId, project.Id, "handout.txt", "project bytes");

        var threadResponse = await client.Post($"{Space(owner.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest("Chat", project.Id));
        threadResponse.StatusCode.Should().Be(HttpStatusCode.Created, await threadResponse.Content.ReadAsStringAsync(Token));
        var thread = (await threadResponse.Content.ReadFromJsonAsync<ThreadSummary>(Token))!;

        var removed = await client.Delete($"{Space(owner.SpaceId)}/projects/{project.Id}");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.Get($"{Space(owner.SpaceId)}/projects/{project.Id}/files/{projectFile.File.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.Get($"{Space(owner.SpaceId)}/files/{projectFile.File.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var threadAfter = await GetFor<ThreadSummary>(client, $"{Space(owner.SpaceId)}/threads/{thread.Id}");
        threadAfter.ProjectId.Should().BeNull();
        threadAfter.Title.Should().Be("Chat");

        var spaceDir = Path.Combine(DroverFactory.FilesPath(KeyRingPath), owner.SpaceId.ToString("N"));
        (Directory.Exists(spaceDir) ? Directory.GetFiles(spaceDir) : []).Should().BeEmpty();
        (await ReadStoredBytes(owner.SpaceId, owner.UserId)).Should().Be(0);
    }

    #endregion

    #region Private Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static string UploadPath(Guid spaceId, Guid projectId, string name) =>
        $"{Space(spaceId)}/projects/{projectId}/files?name={Uri.EscapeDataString(name)}";

    private static ByteArrayContent TextBody(string text) => new(Encoding.UTF8.GetBytes(text));

    private static void ShouldBeSafe(HttpResponseMessage response)
    {
        var policy = string.Join(' ', response.Headers.GetValues("Content-Security-Policy"));
        policy.Should().Contain("sandbox").And.Contain("default-src 'none'").And.NotContain("script-src");
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("Cross-Origin-Resource-Policy").Should().Equal("same-origin");
    }

    private Task<ApiClient> SignIn((string Email, string Password, Guid UserId, Guid SpaceId) person) =>
        SignIn(person.Email, person.Password);

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<ProjectSummary> CreateProject(ApiClient client, Guid spaceId, string name)
    {
        var response = await client.Post($"{Space(spaceId)}/projects", new CreateProjectRequest(name));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<ProjectSummary>(Token))!;
    }

    private static async Task<BotSummary> CreateBot(ApiClient client, Guid spaceId)
    {
        var connectionResponse = await client.Post(
            $"{Space(spaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted"));
        connectionResponse.StatusCode.Should().Be(HttpStatusCode.Created, await connectionResponse.Content.ReadAsStringAsync(Token));
        var connection = (await connectionResponse.Content.ReadFromJsonAsync<ModelConnectionSummary>(Token))!;

        var botResponse = await client.Post($"{Space(spaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null));
        botResponse.StatusCode.Should().Be(HttpStatusCode.Created, await botResponse.Content.ReadAsStringAsync(Token));
        return (await botResponse.Content.ReadFromJsonAsync<BotSummary>(Token))!;
    }

    private static async Task<ProjectFileResponse> UploadProjectFile(ApiClient client, Guid spaceId, Guid projectId, string name, string text) =>
        await UploadProjectFileBytes(client, spaceId, projectId, name, Encoding.UTF8.GetBytes(text));

    private static async Task<ProjectFileResponse> UploadProjectFileBytes(ApiClient client, Guid spaceId, Guid projectId, string name, byte[] bytes)
    {
        var response = await client.PostContent(UploadPath(spaceId, projectId, name), new ByteArrayContent(bytes));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        response.Headers.Location!.ToString().Should().StartWith($"/api/spaces/{spaceId}/projects/{projectId}/files/");
        return (await response.Content.ReadFromJsonAsync<ProjectFileResponse>(Token))!;
    }

    private static async Task<T> GetFor<T>(ApiClient client, string path)
    {
        var response = await client.Get(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private async Task<long> ReadStoredBytes(Guid spaceId, Guid userId)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        return await scope.ServiceProvider.GetRequiredService<FileRepository>().GetStoredBytes(spaceId, Token);
    }

    #endregion

    #region Nested Types

    private sealed class UnmeasuredContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes, 0, bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    #endregion
}
