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
    public async Task Asking_whether_setup_or_registration_is_open_does_not_spend_the_sign_in_allowance()
    {
        var client = NewClient("192.0.2.50");

        for (var ask = 0; ask < 6; ask++)
        {
            (await client.Get("/api/setup")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.Get("/api/register")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        (await client.SignIn("nobody@example.test", "not-the-password-1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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

/// <summary>What the forwarded-headers options hold before any request arrives.</summary>
public sealed class TrustedProxyConfigurationTests
{
    #region Public Methods

    [Fact]
    public void With_no_proxy_named_no_forwarded_header_is_read_by_anything()
    {
        var options = Configure([]);

        options.ForwardedHeaders.Should().Be(ForwardedHeaders.None);
        options.KnownProxies.Should().BeEmpty();
        options.KnownIPNetworks.Should().BeEmpty();
    }

    [Fact]
    public void A_named_proxy_is_the_only_one_trusted_and_for_one_hop()
    {
        var options = Configure(new() { ["Drover:Proxy:TrustedAddresses:0"] = "203.0.113.9" });

        options.ForwardedHeaders.Should().Be(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        options.KnownProxies.Should().Equal(IPAddress.Parse("203.0.113.9"));
        options.KnownIPNetworks.Should().BeEmpty();
        options.ForwardLimit.Should().Be(1);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public void The_server_does_not_start_with_the_hosts_own_forwarded_headers_switch_on(string value)
    {
        var configure = () => Configure(new() { [TrustedProxies.HostSwitch] = value });

        configure.Should().Throw<InvalidOperationException>().WithMessage("*ASPNETCORE_FORWARDEDHEADERS_ENABLED*");
    }

    [Fact]
    public void An_entry_that_is_not_an_address_or_a_network_stops_the_server()
    {
        var configure = () => Configure(new() { ["Drover:Proxy:TrustedAddresses:0"] = "proxy.example.test" });

        configure.Should().Throw<InvalidOperationException>().WithMessage("*proxy.example.test*");
    }

    #endregion

    #region Private Methods

    private static ForwardedHeadersOptions Configure(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var provider = new ServiceCollection().AddTrustedProxies(configuration).BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }

    #endregion
}
