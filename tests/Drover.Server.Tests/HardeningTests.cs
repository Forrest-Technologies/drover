namespace Drover.Server.Tests;

public sealed class HardeningTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Public Methods

    [Fact]
    public async Task Every_response_carries_the_security_headers_and_a_policy_with_no_inline_script()
    {
        var response = await NewClient().Get("/api/setup");

        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        policy.Should().Contain("script-src 'self' 'wasm-unsafe-eval'").And.Contain("object-src 'none'")
            .And.Contain("frame-ancestors 'none'");
        policy.Should().NotContain("http:").And.NotContain("https:");

        // Inline styles are allowed so a browser extension's own interface keeps its look. Nothing else is inline:
        // every directive that can run code stays closed.
        var directives = policy.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        directives.Where(directive => directive.Contains("unsafe-inline")).Should().Equal("style-src 'self' 'unsafe-inline'");
        directives.Should().Contain("default-src 'self'").And.NotContain(directive => directive.Contains("unsafe-eval'") && !directive.Contains("wasm-unsafe-eval"));
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle("no-referrer");
    }

    [Fact]
    public async Task A_response_over_HTTPS_pins_the_name_to_HTTPS_and_one_over_plain_HTTP_does_not()
    {
        var secure = await NewClient().Get("/api/setup");
        var plain = await NewClient(scheme: "http").Get("/api/setup");

        secure.Headers.GetValues("Strict-Transport-Security").Should().ContainSingle("max-age=15552000");
        plain.Headers.Contains("Strict-Transport-Security").Should().BeFalse();
    }

    [Fact]
    public async Task The_api_is_described_by_an_OpenAPI_document()
    {
        var response = await NewClient().Get("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync(Token);
        text.Should().Contain("/api/sign-in").And.Contain("/api/setup").And.Contain("/api/spaces/{spaceId}");
    }

    [Theory]
    [InlineData(2 * 1024 * 1024)]
    [InlineData(100 * 1024)]
    public async Task An_oversized_anonymous_sign_in_is_refused_before_its_body_is_read(int size)
    {
        var response = await NewClient().PostContent("/api/sign-in", Body(size));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Theory]
    [InlineData("/api/password-reset")]
    [InlineData("/api/setup")]
    [InlineData("/api/register")]
    public async Task Every_account_endpoint_takes_at_most_64_KB(string path)
    {
        var over = await NewClient().PostContent(path, Body((64 * 1024) + 1));
        var at = await NewClient().PostContent(path, Body(64 * 1024));

        over.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        at.StatusCode.Should().NotBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task A_normal_sign_in_still_works()
    {
        var (_, _, password) = await SetUpFirstAccount();

        var response = await NewClient().SignIn("owner@example.test", password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Any_other_api_body_over_1_MB_is_refused_and_one_under_it_is_not()
    {
        var path = $"/api/spaces/{Guid.NewGuid()}/bots";

        var over = await NewClient().PostContent(path, Body((1024 * 1024) + 1));
        var under = await NewClient().PostContent(path, Body(900 * 1024));

        over.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        under.StatusCode.Should().NotBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public void The_server_itself_caps_bodies_at_1_MB_and_sends_no_server_header()
    {
        var kestrel = Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        kestrel.Limits.MaxRequestBodySize.Should().Be(1024 * 1024);
        kestrel.AddServerHeader.Should().BeFalse();
    }

    [Fact]
    public async Task No_response_names_the_server()
    {
        var response = await NewClient().Get("/api/setup");

        response.Headers.Contains("Server").Should().BeFalse();
    }

    [Fact]
    public async Task Every_api_response_is_not_to_be_stored_signed_in_or_not()
    {
        var (owner, _, _) = await SetUpFirstAccount();

        var anonymous = await NewClient().Get("/api/setup");
        var signedIn = await owner.Get("/api/session");
        var missing = await NewClient().Get("/api/nothing-here");

        HttpResponseMessage[] responses = [anonymous, signedIn, missing];
        foreach (var response in responses)
        {
            response.Headers.CacheControl!.NoStore.Should().BeTrue(response.RequestMessage!.RequestUri!.ToString());
        }
    }

    [Fact]
    public async Task An_unknown_api_path_is_404_not_the_web_client()
    {
        var response = await NewClient().Get("/api/nothing-here");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Private Methods

    /// <summary>A JSON-shaped body of exactly <paramref name="size"/> bytes, with its length declared.</summary>
    private static ByteArrayContent Body(int size)
    {
        var bytes = new byte[size];
        bytes.AsSpan().Fill((byte)' ');
        "{}"u8.CopyTo(bytes);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/json");
        return content;
    }

    #endregion
}

/// <summary>The API description is for building Drover, not for strangers to a running server.</summary>
public sealed class ProductionHardeningTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["environment"] = "Production",
    };

    #endregion

    #region Public Methods

    [Fact]
    public async Task The_openapi_document_is_not_served_in_production()
    {
        var response = await NewClient().Get("/openapi/v1.json");

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).Should().NotContain("/api/sign-in");
    }

    #endregion
}
