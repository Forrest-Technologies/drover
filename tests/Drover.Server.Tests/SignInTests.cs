namespace Drover.Server.Tests;

public sealed class SignInTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Public Methods

    [Fact]
    public async Task A_person_can_sign_in_and_see_their_spaces()
    {
        var person = await SeedPerson("alice");
        var client = NewClient();

        var response = await client.SignIn(person.Email, person.Password);
        var session = await client.Get("/api/session");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await session.Content.ReadFromJsonAsync<SessionResponse>(Token);
        body!.UserId.Should().Be(person.UserId);
        body.Spaces.Should().ContainSingle().Which.Id.Should().Be(person.SpaceId);
    }

    [Fact]
    public async Task The_session_cookie_is_http_only_secure_and_host_bound()
    {
        var person = await SeedPerson("alice");
        var client = NewClient();

        var response = await client.SignIn(person.Email, person.Password);

        var cookie = response.Headers.GetValues("Set-Cookie").Single(line => line.StartsWith("__Host-Drover.Session=", StringComparison.Ordinal));
        cookie.ToLowerInvariant().Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=lax").And.Contain("path=/");
    }

    [Fact]
    public async Task A_wrong_password_an_unknown_email_and_a_locked_account_are_refused_alike()
    {
        var person = await SeedPerson("alice");
        var client = NewClient();

        var wrong = await client.SignIn(person.Email, "not-the-password-1");
        var unknown = await client.SignIn("nobody@example.test", "not-the-password-1");

        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await wrong.Content.ReadAsStringAsync(Token)).Should().Be(await unknown.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_password()
    {
        var person = await SeedPerson("alice");
        var client = NewClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await client.SignIn(person.Email, "not-the-password-1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var locked = await client.SignIn(person.Email, person.Password);

        locked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_right_password_resets_the_count_of_wrong_ones()
    {
        var person = await SeedPerson("alice");
        var client = NewClient();

        for (var round = 0; round < 3; round++)
        {
            await client.SignIn(person.Email, "not-the-password-1");
            await client.SignIn(person.Email, "not-the-password-1");
            (await client.SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Signing_in_without_an_antiforgery_token_is_refused()
    {
        var person = await SeedPerson("alice");
        var client = NewClient();

        var response = await client.PostWithoutToken("/api/sign-in", new SignInRequest(person.Email, person.Password));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_needs_the_token_and_ends_the_session()
    {
        var person = await SeedPerson("alice");
        var client = NewClient();
        await client.SignIn(person.Email, person.Password);

        var withoutToken = await client.PostWithoutToken("/api/sign-out");
        var stillIn = await client.Get("/api/session");
        var signedOut = await client.Post("/api/sign-out");
        var after = await client.Get("/api/session");

        withoutToken.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        stillIn.StatusCode.Should().Be(HttpStatusCode.OK);
        signedOut.StatusCode.Should().Be(HttpStatusCode.NoContent);
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_in_issues_a_fresh_antiforgery_token()
    {
        var person = await SeedPerson("alice");
        var client = NewClient();
        var before = await client.Get("/api/antiforgery");
        before.EnsureSuccessStatusCode();
        var stale = client.Cookie("XSRF-TOKEN");

        await client.SignIn(person.Email, person.Password);

        client.Cookie("XSRF-TOKEN").Should().NotBe(stale);
    }

    [Fact]
    public async Task The_session_endpoint_needs_a_session()
    {
        var client = NewClient();

        (await client.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion
}

public sealed class SignInRateLimitTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Drover:SignInRateLimit:PermitLimit"] = "3",
        ["Drover:SignInRateLimit:Window"] = "00:10:00",
    };

    #endregion

    #region Public Methods

    [Fact]
    public async Task Sign_in_is_rate_limited_per_address()
    {
        var client = NewClient("198.51.100.4");
        var other = NewClient("198.51.100.5");
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            statuses.Add((await client.SignIn("nobody@example.test", "not-the-password-1")).StatusCode);
        }

        statuses.Take(3).Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
        statuses.Skip(3).Should().AllBeEquivalentTo(HttpStatusCode.TooManyRequests);
        (await other.SignIn("nobody@example.test", "not-the-password-1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion
}
