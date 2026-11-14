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
        policy.Should().NotContain("unsafe-inline").And.NotContain("http:").And.NotContain("https:");
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

    [Fact]
    public async Task An_unknown_api_path_is_404_not_the_web_client()
    {
        var response = await NewClient().Get("/api/nothing-here");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
