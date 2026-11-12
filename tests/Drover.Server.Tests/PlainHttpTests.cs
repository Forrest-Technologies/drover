namespace Drover.Server.Tests;

/// <summary>
/// The compose deployment serves plain HTTP on localhost. Sign-in must work there, and no cookie may lose
/// <c>Secure</c> to make it work.
/// </summary>
public sealed class PlainHttpTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Public Methods

    [Fact]
    public async Task The_antiforgery_token_is_issued_over_plain_http_and_its_cookies_are_secure()
    {
        var response = await NewClient(scheme: "http").Get("/api/antiforgery");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        SetCookies(response).Should().HaveCount(2).And.OnlyContain(cookie => cookie.Secure);
    }

    [Fact]
    public async Task Setup_and_sign_in_work_over_plain_http_and_every_cookie_is_secure()
    {
        var client = NewClient("127.0.0.1", scheme: "http");
        var password = NewPassword();
        var code = Services.GetRequiredService<SetupCode>().Display;

        var setup = await client.Post("/api/setup", new SetupRequest("owner@example.test", "Owner", password, code));
        var signIn = await NewClient("127.0.0.1", scheme: "http").SignIn("owner@example.test", password);

        setup.StatusCode.Should().Be(HttpStatusCode.Created);
        signIn.StatusCode.Should().Be(HttpStatusCode.OK);
        SetCookies(setup).Should().NotBeEmpty().And.OnlyContain(cookie => cookie.Secure);
        SetCookies(signIn).Should().NotBeEmpty().And.OnlyContain(cookie => cookie.Secure);
        SetCookies(signIn).Should().Contain(cookie => cookie.Name == "__Host-Drover.Session" && cookie.HttpOnly);
    }

    [Fact]
    public async Task A_forwarded_scheme_from_an_address_that_is_not_a_trusted_proxy_changes_nothing()
    {
        var client = NewClient("203.0.113.9", scheme: "http");
        client.ForwardedHeaders["X-Forwarded-Proto"] = "https";

        var document = await (await client.Get("/openapi/v1.json")).Content.ReadAsStringAsync(Token);

        document.Should().Contain("http://localhost").And.NotContain("https://localhost");
    }

    #endregion

    #region Private Methods

    private static IList<SetCookieHeaderValue> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var lines) ? SetCookieHeaderValue.ParseList([.. lines]) : [];

    #endregion
}

/// <summary>A server whose operator named one reverse proxy.</summary>
public sealed class TrustedProxyTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string Proxy = "203.0.113.9";

    #endregion

    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Drover:Proxy:TrustedAddresses:0"] = Proxy,
        ["Drover:SignInRateLimit:PermitLimit"] = "2",
        ["Drover:SignInRateLimit:Window"] = "00:10:00",
    };

    #endregion

    #region Public Methods

    [Fact]
    public async Task The_scheme_a_trusted_proxy_forwards_is_the_scheme_of_the_request()
    {
        var client = NewClient(Proxy, scheme: "http");
        client.ForwardedHeaders["X-Forwarded-Proto"] = "https";

        var document = await (await client.Get("/openapi/v1.json")).Content.ReadAsStringAsync(Token);

        document.Should().Contain("https://localhost");
    }

    [Fact]
    public async Task The_sign_in_limit_counts_the_address_a_trusted_proxy_forwards_not_the_proxy()
    {
        var statuses = new List<HttpStatusCode>();

        for (var person = 1; person <= 4; person++)
        {
            var client = NewClient(Proxy);
            client.ForwardedHeaders["X-Forwarded-For"] = $"198.51.100.{person}";
            statuses.Add((await client.SignIn("nobody@example.test", "not-the-password-1")).StatusCode);
        }

        statuses.Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_address_forwarded_by_anyone_else_is_ignored_so_the_limit_cannot_be_dodged()
    {
        var statuses = new List<HttpStatusCode>();

        for (var claim = 1; claim <= 4; claim++)
        {
            var client = NewClient("192.0.2.77");
            client.ForwardedHeaders["X-Forwarded-For"] = $"198.51.100.{claim}";
            statuses.Add((await client.SignIn("nobody@example.test", "not-the-password-1")).StatusCode);
        }

        statuses.Take(2).Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
        statuses.Skip(2).Should().AllBeEquivalentTo(HttpStatusCode.TooManyRequests);
    }

    #endregion
}
