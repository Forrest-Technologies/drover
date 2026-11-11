namespace Drover.Server.Tests;

public sealed class FirstRunTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Public Methods

    [Fact]
    public async Task Setup_works_exactly_once_and_afterwards_the_setup_endpoints_return_404()
    {
        var client = NewClient("127.0.0.1");

        var open = await client.Get("/api/setup");
        var created = await client.Post("/api/setup", new SetupRequest("owner@example.test", "Owner", NewPassword(), null));
        var closedStatus = await client.Get("/api/setup");
        var closedCreate = await client.Post("/api/setup", new SetupRequest("again@example.test", "Again", NewPassword(), null));

        open.StatusCode.Should().Be(HttpStatusCode.OK);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        closedStatus.StatusCode.Should().Be(HttpStatusCode.NotFound);
        closedCreate.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Two_setups_at_the_same_moment_create_one_account()
    {
        var first = NewClient("127.0.0.1");
        var second = NewClient("127.0.0.1");
        (await first.Get("/api/antiforgery")).EnsureSuccessStatusCode();
        (await second.Get("/api/antiforgery")).EnsureSuccessStatusCode();

        var statuses = await Task.WhenAll(
            first.Post("/api/setup", new SetupRequest("a@example.test", "A", NewPassword(), null)),
            second.Post("/api/setup", new SetupRequest("b@example.test", "B", NewPassword(), null)));

        statuses.Select(response => response.StatusCode).Should()
            .BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.NotFound]);
    }

    [Fact]
    public async Task Remote_setup_without_the_code_is_refused_and_with_it_works()
    {
        var remote = NewClient("203.0.113.9");
        var code = Services.GetRequiredService<SetupCode>().Display;

        var status = await (await remote.Get("/api/setup")).Content.ReadFromJsonAsync<SetupStatus>(Token);
        var missing = await remote.Post("/api/setup", new SetupRequest("o@example.test", "Owner", NewPassword(), null));
        var wrong = await remote.Post("/api/setup", new SetupRequest("o@example.test", "Owner", NewPassword(), "AAAA-AAAA-AAAA"));
        var right = await remote.Post("/api/setup", new SetupRequest("o@example.test", "Owner", NewPassword(), code));

        status!.RequiresCode.Should().BeTrue();
        missing.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        wrong.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        right.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public void A_request_that_a_proxy_forwarded_is_not_treated_as_this_machine()
    {
        SetupCode.IsFromThisMachine(ContextFrom(IPAddress.Loopback, forwardedFor: null)).Should().BeTrue();
        SetupCode.IsFromThisMachine(ContextFrom(IPAddress.Loopback, forwardedFor: "203.0.113.9")).Should().BeFalse();
        SetupCode.IsFromThisMachine(ContextFrom(IPAddress.Parse("203.0.113.9"), forwardedFor: null)).Should().BeFalse();
        SetupCode.IsFromThisMachine(ContextFrom(null, forwardedFor: null)).Should().BeFalse();
    }

    [Fact]
    public void The_setup_code_is_printed_to_the_log_at_start()
    {
        var code = Services.GetRequiredService<SetupCode>().Display;

        Logs.AllText().Should().Contain(code);
    }

    [Fact]
    public async Task Setup_creates_the_user_a_personal_space_and_the_owner_membership()
    {
        var (_, session, _) = await SetUpFirstAccount();

        session.Spaces.Should().ContainSingle();
        session.Spaces[0].Kind.Should().Be("personal");
        session.Spaces[0].Role.Should().Be("owner");
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(session.Spaces[0].Id, session.UserId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        (await db.Memberships.CountAsync(Token)).Should().Be(1);
        (await db.Spaces.CountAsync(Token)).Should().Be(1);
    }

    [Fact]
    public async Task A_weak_password_creates_nothing_and_leaves_setup_open()
    {
        var client = NewClient("127.0.0.1");

        var refused = await client.Post("/api/setup", new SetupRequest("o@example.test", "Owner", "short", null));
        var stillOpen = await client.Get("/api/setup");

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        stillOpen.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Setup_without_an_antiforgery_token_is_refused()
    {
        var client = NewClient("127.0.0.1");

        var response = await client.PostWithoutToken("/api/setup", new SetupRequest("o@example.test", "Owner", NewPassword(), null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.Get("/api/setup")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Private Methods

    private static DefaultHttpContext ContextFrom(IPAddress? address, string? forwardedFor)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = address;

        if (forwardedFor is not null)
        {
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        }

        return context;
    }

    #endregion
}
