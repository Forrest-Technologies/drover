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

/// <summary>
/// A server whose operator named the proxy's network rather than its address, as on a platform whose proxy reaches
/// the server from an address that changes. Only an address inside a named network is believed.
/// </summary>
public sealed class TrustedProxyNetworkTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Drover:Proxy:TrustedAddresses:0"] = "10.20.0.0/16",
        ["Drover:Proxy:TrustedAddresses:1"] = "fd00:20::/64",
        ["Drover:SignInRateLimit:PermitLimit"] = "2",
        ["Drover:SignInRateLimit:Window"] = "00:10:00",
    };

    #endregion

    #region Public Methods

    [Theory]
    [InlineData("10.20.3.4")]
    [InlineData("10.20.255.1")]
    [InlineData("::ffff:10.20.3.4")]
    [InlineData("fd00:20::7")]
    public async Task The_scheme_forwarded_from_inside_a_trusted_network_is_the_scheme_of_the_request(string proxy)
    {
        var client = NewClient(proxy, scheme: "http");
        client.ForwardedHeaders["X-Forwarded-Proto"] = "https";

        var document = await (await client.Get("/openapi/v1.json")).Content.ReadAsStringAsync(Token);

        document.Should().Contain("https://localhost");
    }

    [Theory]
    [InlineData("10.21.0.1")]
    [InlineData("192.0.2.9")]
    [InlineData("::ffff:10.21.0.1")]
    [InlineData("::ffff:203.0.113.5")]
    [InlineData("fd00:21::7")]
    [InlineData("2001:db8::9")]
    public async Task The_scheme_forwarded_from_outside_every_trusted_network_is_ignored(string stranger)
    {
        var client = NewClient(stranger, scheme: "http");
        client.ForwardedHeaders["X-Forwarded-Proto"] = "https";

        var document = await (await client.Get("/openapi/v1.json")).Content.ReadAsStringAsync(Token);

        document.Should().Contain("http://localhost").And.NotContain("https://localhost");
    }

    [Theory]
    [InlineData("10.20.3.4", "10.20.9.9")]
    [InlineData("fd00:20::7", "fd00:20::8")]
    public async Task The_sign_in_limit_counts_each_person_behind_a_proxy_whose_address_changes(string first, string second)
    {
        var statuses = new List<HttpStatusCode>();

        for (var person = 1; person <= 4; person++)
        {
            var client = NewClient(person % 2 is 0 ? first : second, scheme: "http");
            client.ForwardedHeaders["X-Forwarded-For"] = $"198.51.100.{person}";
            client.ForwardedHeaders["X-Forwarded-Proto"] = "https";
            var response = await client.SignIn("nobody@example.test", "not-the-password-1");
            statuses.Add(response.StatusCode);
        }

        statuses.Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("10.21.0.1")]
    [InlineData("::ffff:203.0.113.5")]
    [InlineData("fd00:21::7")]
    public async Task An_address_forwarded_from_outside_every_trusted_network_is_ignored(string stranger)
    {
        var statuses = new List<HttpStatusCode>();

        for (var claim = 1; claim <= 4; claim++)
        {
            var client = NewClient(stranger);
            client.ForwardedHeaders["X-Forwarded-For"] = $"198.51.100.{claim}";
            statuses.Add((await client.SignIn("nobody@example.test", "not-the-password-1")).StatusCode);
        }

        statuses.Take(2).Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
        statuses.Skip(2).Should().AllBeEquivalentTo(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Sign_in_through_a_proxy_inside_a_trusted_network_sets_only_secure_cookies()
    {
        var (_, _, password) = await SetUpFirstAccount();
        var client = NewClient("10.20.3.4", scheme: "http");
        client.ForwardedHeaders["X-Forwarded-For"] = "198.51.100.40";
        client.ForwardedHeaders["X-Forwarded-Proto"] = "https";

        var signIn = await client.SignIn("owner@example.test", password);

        signIn.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookies = signIn.Headers.TryGetValues("Set-Cookie", out var lines) ? SetCookieHeaderValue.ParseList([.. lines]) : [];
        cookies.Should().NotBeEmpty().And.OnlyContain(cookie => cookie.Secure);
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

    [Fact]
    public void A_named_network_is_trusted_as_a_network_in_both_address_families()
    {
        var options = Configure(new()
        {
            ["Drover:Proxy:TrustedAddresses:0"] = "10.20.0.0/16",
            ["Drover:Proxy:TrustedAddresses:1"] = "fd00:20::/64",
            ["Drover:Proxy:TrustedAddresses:2"] = "2001:db8::1",
        });

        options.KnownIPNetworks.Should().Equal(System.Net.IPNetwork.Parse("10.20.0.0/16"), System.Net.IPNetwork.Parse("fd00:20::/64"));
        options.KnownProxies.Should().Equal(IPAddress.Parse("2001:db8::1"));
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("10.0.0.0/7")]
    [InlineData("::/0")]
    [InlineData("fd00::/31")]
    public void A_network_so_wide_it_trusts_strangers_stops_the_server_and_says_why(string entry)
    {
        var configure = () => Configure(new() { ["Drover:Proxy:TrustedAddresses:0"] = entry });

        configure.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{entry}*")
            .WithMessage("*too wide*")
            .WithMessage("*any address in it could claim*");
    }

    [Theory]
    [InlineData("10.0.0.0/8")]
    [InlineData("fd00::/32")]
    public void The_widest_network_allowed_is_accepted(string entry)
    {
        var options = Configure(new() { ["Drover:Proxy:TrustedAddresses:0"] = entry });

        options.KnownIPNetworks.Should().Equal(System.Net.IPNetwork.Parse(entry));
    }

    [Theory]
    [InlineData("::ffff:0:0/96")]
    [InlineData("::ffff:10.0.0.0/104")]
    [InlineData("::ffff:0:0/100")]
    [InlineData("::/32")]
    [InlineData("::/80")]
    [InlineData("::/96")]
    public void An_ipv6_network_that_takes_in_ipv4_addresses_stops_the_server_and_names_the_way_to_write_it(string entry)
    {
        var configure = () => Configure(new() { ["Drover:Proxy:TrustedAddresses:0"] = entry });

        configure.Should().Throw<InvalidOperationException>()
            .WithMessage($"*'{entry}'*")
            .WithMessage("*IPv4 network*");
    }

    [Theory]
    [InlineData("010.0.0.0/8")]
    [InlineData("10.0.0.0/08")]
    [InlineData("10.0.0.0/+8")]
    [InlineData("10.0.0.0/ 8")]
    [InlineData("10.1/16")]
    [InlineData("10.0.0.010")]
    [InlineData("0x0a.0.0.1")]
    [InlineData("10.0.0.1%1")]
    [InlineData("fe80::1%1")]
    [InlineData("::ffff:010.0.0.1")]
    public void An_entry_not_written_in_plain_form_stops_the_server(string entry)
    {
        var configure = () => Configure(new() { ["Drover:Proxy:TrustedAddresses:0"] = entry });

        configure.Should().Throw<InvalidOperationException>().WithMessage($"*'{entry}'*").WithMessage("*plain*");
    }

    [Fact]
    public void An_ipv4_mapped_address_is_still_accepted_as_one_exact_proxy()
    {
        var options = Configure(new() { ["Drover:Proxy:TrustedAddresses:0"] = "::ffff:10.0.0.5" });

        options.KnownProxies.Should().Equal(IPAddress.Parse("::ffff:10.0.0.5"));
    }

    [Fact]
    public void A_network_written_with_a_host_address_stops_the_server_and_names_the_network_meant()
    {
        var configure = () => Configure(new() { ["Drover:Proxy:TrustedAddresses:0"] = "10.20.3.4/16" });

        configure.Should().Throw<InvalidOperationException>().WithMessage("*10.20.3.4/16*").WithMessage("*10.20.0.0/16*");
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
