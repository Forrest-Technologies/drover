namespace Drover.Server.Tests.Decisions;

/// <summary>
/// The space's decision model through its routes over real PostgreSQL, with a scripted provider: who may read and
/// change it, that another space sees nothing, that the key is never returned or written down, what a test records,
/// and what is refused. Every value here is a sample.
/// </summary>
public sealed class DecisionModelApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string Address = "https://judge.example.test/api/systemone";

    private readonly ScriptedDecisionProvider scripted = new();

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IDecisionModelProvider>(scripted);

    #endregion

    #region Who

    [Fact]
    public async Task A_member_reads_and_cannot_change()
    {
        var owner = await SeedPerson("owner");
        var ownerClient = await SignIn(owner.Email, owner.Password);
        await Send<DecisionModelResponse>(ownerClient.Put(Route(owner.SpaceId), Connect()), HttpStatusCode.OK);
        var member = await SeedMember(owner.SpaceId, "member", MembershipRole.Member);
        var memberClient = await SignIn(member.Email, member.Password);

        var read = await Send<DecisionModelState>(memberClient.Get(Route(owner.SpaceId)), HttpStatusCode.OK);
        var set = await memberClient.Put(Route(owner.SpaceId), Connect());
        var key = await memberClient.Put($"{Route(owner.SpaceId)}/key", new ReplaceDecisionKeyRequest(Key()));
        var test = await memberClient.Post($"{Route(owner.SpaceId)}/test", new { });
        var disconnect = await memberClient.Delete(Route(owner.SpaceId));

        read.Connection!.Model.Should().Be("judge-1");
        new[] { set.StatusCode, key.StatusCode, test.StatusCode, disconnect.StatusCode }.Should().OnlyContain(status => status == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Another_space_gets_404()
    {
        var alice = await SeedPerson("alice");
        var aliceClient = await SignIn(alice.Email, alice.Password);
        await Send<DecisionModelResponse>(aliceClient.Put(Route(alice.SpaceId), Connect()), HttpStatusCode.OK);
        var bob = await SeedPerson("bob");
        var bobClient = await SignIn(bob.Email, bob.Password);

        var responses = new[]
        {
            await bobClient.Get(Route(alice.SpaceId)),
            await bobClient.Put(Route(alice.SpaceId), Connect()),
            await bobClient.Put($"{Route(alice.SpaceId)}/key", new ReplaceDecisionKeyRequest(Key())),
            await bobClient.Post($"{Route(alice.SpaceId)}/test", new { }),
            await bobClient.Delete(Route(alice.SpaceId)),
        };
        var own = await Send<DecisionModelState>(bobClient.Get(Route(bob.SpaceId)), HttpStatusCode.OK);

        responses.Select(response => response.StatusCode).Should().OnlyContain(status => status == HttpStatusCode.NotFound);
        own.Connection.Should().BeNull("Bob's space has none, and Alice's is not visible from it");
    }

    #endregion

    #region The Key

    [Fact]
    public async Task The_key_is_never_returned()
    {
        var owner = await SeedPerson("owner");
        var client = await SignIn(owner.Email, owner.Password);
        var key = Key();
        var newKey = Key();

        var set = await client.Put(Route(owner.SpaceId), Connect(key));
        var replaced = await client.Put($"{Route(owner.SpaceId)}/key", new ReplaceDecisionKeyRequest(newKey));
        var tested = await client.Post($"{Route(owner.SpaceId)}/test", new { });
        var read = await client.Get(Route(owner.SpaceId));
        var disconnected = await client.Delete(Route(owner.SpaceId));
        var bodies = new[] { set, replaced, tested, read, disconnected }.Select(response => response.Content.ReadAsStringAsync(Token));
        var audit = await ReadAudit(owner.SpaceId, owner.UserId);

        (await Task.WhenAll(bodies)).Should().OnlyContain(body => !body.Contains(key) && !body.Contains(newKey));
        (await Send<DecisionModelResponse>(Task.FromResult(set), HttpStatusCode.OK)).HasKey.Should().BeTrue();
        audit.Select(entry => entry.Action).Should().Equal("decision_model.connected", "decision_model.key_replaced", "decision_model.disconnected");
        audit.Should().OnlyContain(entry => !entry.Data.GetRawText().Contains(key) && !entry.Data.GetRawText().Contains(newKey));
        scripted.Tests.Should().ContainSingle().Which.Key.Should().Be(newKey, "the test is given the key now stored, and only the provider sees it");
    }

    [Fact]
    public async Task Replacing_the_key_returns_the_connection_to_pending()
    {
        var owner = await SeedPerson("owner");
        var client = await SignIn(owner.Email, owner.Password);
        await Send<DecisionModelResponse>(client.Put(Route(owner.SpaceId), Connect()), HttpStatusCode.OK);
        (await Send<DecisionModelTestResponse>(client.Post($"{Route(owner.SpaceId)}/test", new { }), HttpStatusCode.OK)).Connection.Status.Should().Be("ready");

        var replaced = await Send<DecisionModelResponse>(client.Put($"{Route(owner.SpaceId)}/key", new ReplaceDecisionKeyRequest(Key())), HttpStatusCode.OK);

        replaced.Status.Should().Be("pending");
        replaced.TestedAt.Should().BeNull();
    }

    #endregion

    #region Testing

    [Fact]
    public async Task A_test_records_ready_or_failed_with_the_reason()
    {
        var owner = await SeedPerson("owner");
        var client = await SignIn(owner.Email, owner.Password);
        await Send<DecisionModelResponse>(client.Put(Route(owner.SpaceId), Connect()), HttpStatusCode.OK);

        var passed = await Send<DecisionModelTestResponse>(client.Post($"{Route(owner.SpaceId)}/test", new { }), HttpStatusCode.OK);
        scripted.TestResult = ConnectionTestResult.Failed(ModelFailure.InvalidKey, "The provider rejected the key.");
        var failed = await Send<DecisionModelTestResponse>(client.Post($"{Route(owner.SpaceId)}/test", new { }), HttpStatusCode.OK);
        scripted.TestResult = ConnectionTestResult.Failed(ModelFailure.RetryLater, "Try again in a minute.");
        var limited = await Send<DecisionModelTestResponse>(client.Post($"{Route(owner.SpaceId)}/test", new { }), HttpStatusCode.OK);

        passed.Should().Match<DecisionModelTestResponse>(test => test.Succeeded && test.Message == "Connected. Answered in 12 ms." && test.Connection.Status == "ready");
        passed.Connection.TestedAt.Should().NotBeNull();
        failed.Should().Match<DecisionModelTestResponse>(test => !test.Succeeded && test.Failure == "invalid_key" && test.Connection.Status == "failed");
        failed.Connection.LastError.Should().Be("The provider rejected the key.");
        limited.Connection.Status.Should().Be("failed", "a rate limit leaves the status as it was");
        scripted.Tests.Should().OnlyContain(test => !test.ScopeOpen, "no tenant scope is open while the provider answers");
    }

    [Fact]
    public async Task Testing_or_changing_a_space_with_none_is_404_and_reading_it_says_none()
    {
        var owner = await SeedPerson("owner");
        var client = await SignIn(owner.Email, owner.Password);

        var state = await Send<DecisionModelState>(client.Get(Route(owner.SpaceId)), HttpStatusCode.OK);
        var test = await client.Post($"{Route(owner.SpaceId)}/test", new { });
        var key = await client.Put($"{Route(owner.SpaceId)}/key", new ReplaceDecisionKeyRequest(Key()));
        var disconnect = await client.Delete(Route(owner.SpaceId));

        state.Connection.Should().BeNull();
        state.Kinds.Should().Contain(kind => kind.Kind == ScriptedDecisionProvider.ConnectionKind && kind.KeyRule == "required" && kind.AddressRule == "address");
        state.JudgedRulesAllowed.Should().BeTrue();
        new[] { test.StatusCode, key.StatusCode, disconnect.StatusCode }.Should().OnlyContain(status => status == HttpStatusCode.NotFound);
    }

    #endregion

    #region Refused

    [Fact]
    public async Task A_private_address_is_refused_where_forbidden()
    {
        var owner = await SeedPerson("owner");
        var client = await SignIn(owner.Email, owner.Password);
        await InSpace(owner.SpaceId, owner.UserId, async services =>
        {
            var db = services.GetRequiredService<DroverDbContext>();
            var space = await db.Spaces.SingleAsync(found => found.Id == owner.SpaceId, Token);
            space.Settings = space.Settings with { AllowPrivateAddresses = false };
            await db.SaveChangesAsync(Token);
            return true;
        });

        var refused = await client.Put(Route(owner.SpaceId), Connect() with { Address = "http://10.0.0.7/api/systemone" });

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(refused)).Should().Contain("private addresses");
    }

    [Theory]
    [MemberData(nameof(RefusedRequests))]
    public async Task A_request_that_cannot_connect_is_refused_with_the_reason(string what, SetDecisionModelRequest request, string expected)
    {
        var owner = await SeedPerson("owner");
        var client = await SignIn(owner.Email, owner.Password);

        var refused = await client.Put(Route(owner.SpaceId), request);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, what);
        (await Title(refused)).Should().Contain(expected, what);
        (await Send<DecisionModelState>(client.Get(Route(owner.SpaceId)), HttpStatusCode.OK)).Connection.Should().BeNull("nothing is stored of a refused request");
    }

    public static TheoryData<string, SetDecisionModelRequest, string> RefusedRequests() => new()
    {
        { "an unknown kind", new("nobodys_kind", Address, null, "judge-1", "a-key"), "not available on this server" },
        { "no model", new(ScriptedDecisionProvider.ConnectionKind, Address, null, " ", "a-key"), "Name the model" },
        { "no address", new(ScriptedDecisionProvider.ConnectionKind, null, null, "judge-1", "a-key"), "address of the server" },
        { "an address with a key in it", new(ScriptedDecisionProvider.ConnectionKind, "https://user:pass@judge.example.test/", null, "judge-1", "a-key"), "Leave credentials" },
        { "no key where one is needed", new(ScriptedDecisionProvider.ConnectionKind, Address, null, "judge-1", null), "needs a key" },
        { "a key with a line break", new(ScriptedDecisionProvider.ConnectionKind, Address, null, "judge-1", "a\nkey"), "without spaces or line breaks" },
        { "a price below zero", new(ScriptedDecisionProvider.ConnectionKind, Address, null, "judge-1", "a-key", -1), "price" },
    };

    [Fact]
    public async Task Changing_where_it_points_without_a_new_key_is_refused_and_the_model_alone_keeps_the_key()
    {
        var owner = await SeedPerson("owner");
        var client = await SignIn(owner.Email, owner.Password);
        await Send<DecisionModelResponse>(client.Put(Route(owner.SpaceId), Connect()), HttpStatusCode.OK);

        var moved = await client.Put(Route(owner.SpaceId), Connect() with { Address = "https://elsewhere.example.test/api", ApiKey = null });
        var renamed = await Send<DecisionModelResponse>(client.Put(Route(owner.SpaceId), Connect() with { Model = "judge-2", ApiKey = null }), HttpStatusCode.OK);

        moved.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a stored key is never sent somewhere it was not given for");
        renamed.Should().Match<DecisionModelResponse>(connection => connection.Model == "judge-2" && connection.HasKey && connection.Status == "pending");
    }

    #endregion

    #region Private Methods

    private static string Route(Guid spaceId) => $"/api/spaces/{spaceId}/decision-model";

    private static string Key() => $"test-decision-key-{Guid.NewGuid():N}";

    private static SetDecisionModelRequest Connect(string? key = null) =>
        new(ScriptedDecisionProvider.ConnectionKind, Address, null, "judge-1", key ?? Key());

    private static async Task<string?> Title(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString();

    private static async Task<T> Send<T>(Task<HttpResponseMessage> request, HttpStatusCode expected)
    {
        var response = await request;
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private Task<List<AuditLogEntry>> ReadAudit(Guid spaceId, Guid userId) =>
        InSpace(spaceId, userId, services => services.GetRequiredService<DroverDbContext>().AuditLog
            .Where(entry => entry.TargetKind == AuditTargets.DecisionModel)
            .OrderBy(entry => entry.Id)
            .ToListAsync(Token));

    private async Task<T> InSpace<T>(Guid spaceId, Guid userId, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var tenant = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var result = await work(scope.ServiceProvider);
        await tenant.Commit(Token);
        return result;
    }

    #endregion
}
