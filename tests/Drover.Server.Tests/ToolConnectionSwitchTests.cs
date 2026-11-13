namespace Drover.Server.Tests;

/// <summary>An operator who turns tool connections off gets a server that makes none and calls none.</summary>
public sealed class ToolConnectionSwitchTests(PostgresServer postgres) : ToolServerTestBase(postgres)
{
    #region Overrides

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Drover:Policy:Rules:AllowToolConnections"] = "false",
    };

    #endregion

    #region Tests

    [Fact]
    public async Task With_tool_connections_off_none_is_added_and_none_that_exists_is_offered_called_listed_signed_in_to_or_renewed()
    {
        Script = new ScriptedModel([new ToolCallOutput("c1", "notes__echo", """{"text":"x"}""")], [new TextOutput("I could not.")]);
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless));
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await AllowPrivateAddresses(alice.SpaceId);
        var space = $"/api/spaces/{alice.SpaceId}";
        var existing = await SeedConnection(alice.SpaceId, alice.UserId, server.Url);
        var signedIn = await SeedSignInRunningOut(alice.SpaceId, alice.UserId, server.Url);
        var bot = await CreateBot(client, alice.SpaceId);
        await Grant(alice.SpaceId, alice.UserId, bot.Id, "echo");

        var added = await client.Post($"{space}/tool-connections", new CreateToolConnectionRequest("More", server.Url.ToString()));
        var refusal = await added.Content.ReadAsStringAsync(Token);
        var refreshed = await PostFor<ToolConnectionRefresh>(client, $"{space}/tool-connections/{existing}/refresh", new { });
        var thread = await PostFor<ThreadSummary>(client, $"{space}/bots/{bot.Id}/threads", new CreateThreadRequest(null));
        var sent = await PostFor<SendMessageResponse>(client, $"{space}/threads/{thread.Id}/messages", new SendMessageRequest("Use the notes"));
        var run = await WaitForRun(client, alice.SpaceId, sent.Run.Id);
        var signIn = await PostFor<ToolSignInStart>(client, $"{space}/tool-connections/{signedIn.ConnectionId}/sign-in", new { });
        var renewed = await Services.GetRequiredService<ToolSignInRenewal>().RenewDue(Token);
        var triedAt = await RenewalTriedAt(alice.SpaceId, alice.UserId, signedIn.CredentialId);

        added.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        refusal.Should().Contain("AllowToolConnections", "the message names the rule an operator would change");
        refreshed.Problem.Should().Contain("AllowToolConnections");
        run.Status.Should().Be("completed", run.Error?.Message);
        Model!.ToolsOffered[0].Should().NotContain("notes__echo", "a tool that cannot be called is not sent to the model");
        Script.Requests[1].Should().Contain(message => message.Role == "tool", "a model that asks for it anyway is told no");
        signIn.Step.Should().Be("failed", "no sign-in is started");
        signIn.Problem.Should().Contain("AllowToolConnections");
        renewed.Should().Be(0);
        triedAt.Should().BeNull("the renewal does not look for sign-ins at all, so none is marked as tried");
        server.Requests.Should().Be(0, "nothing reaches a tool server while tool connections are off");
    }

    #endregion

    #region Helpers

    /// <summary>A connection with one tool, as if it had been added and listed before tool connections were turned off.</summary>
    private async Task<Guid> SeedConnection(Guid spaceId, Guid userId, Uri url)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        var connection = new ToolConnection { Name = "Notes", Slug = "notes", Url = url.ToString(), Status = ConnectionStatus.Ready, Trust = TrustLevel.Trusted };
        db.ToolConnections.Add(connection);
        db.Tools.Add(
            new Tool
            {
                SourceKind = ToolSourceKind.Mcp,
                ToolConnectionId = connection.Id,
                Name = "echo",
                Description = "Repeats the text it is given.",
                InputSchema = JsonElement.Parse("""{"type":"object","properties":{"text":{"type":"string"}}}"""),
                DefinitionHash = "seeded",
                FirstSeenAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow,
            });
        await db.SaveChangesAsync(Token);
        await work.Commit(Token);
        return connection.Id;
    }

    /// <summary>A sign-in connection whose tokens are about to run out, as the background renewal would find it.</summary>
    private async Task<(Guid ConnectionId, Guid CredentialId)> SeedSignInRunningOut(Guid spaceId, Guid userId, Uri url)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        var connection = new ToolConnection
        {
            Name = "Mail",
            Slug = "mail",
            Url = url.ToString(),
            AuthKind = ToolAuthKind.Oauth,
            CredentialScope = CredentialScope.PerUser,
            Status = ConnectionStatus.Ready,
        };
        var secretId = await scope.ServiceProvider.GetRequiredService<ISecretVault>().Store(SecretKind.OauthTokens, "{}", userId, Token);
        var credential = new ToolConnectionCredential { ToolConnectionId = connection.Id, SecretId = secretId, AudienceUserId = userId, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) };
        db.ToolConnections.Add(connection);
        db.ToolConnectionCredentials.Add(credential);
        await db.SaveChangesAsync(Token);
        await work.Commit(Token);
        return (connection.Id, credential.Id);
    }

    private async Task<DateTimeOffset?> RenewalTriedAt(Guid spaceId, Guid userId, Guid credentialId)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var credential = await scope.ServiceProvider.GetRequiredService<DroverDbContext>().ToolConnectionCredentials.SingleAsync(found => found.Id == credentialId, Token);
        return credential.RenewAttemptedAt;
    }

    #endregion
}
