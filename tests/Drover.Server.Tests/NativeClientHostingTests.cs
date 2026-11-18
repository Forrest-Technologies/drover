namespace Drover.Server.Tests;

/// <summary>The optional native publish is served beside the existing client, under the same security policy.</summary>
public sealed class NativeClientHostingTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings
    {
        get
        {
            var root = Path.Combine(KeyRingPath, "client");
            var native = Path.Combine(root, "native");
            Directory.CreateDirectory(native);
            File.WriteAllText(Path.Combine(native, "index.html"), "<!DOCTYPE html><title>Sample native client</title>");
            File.WriteAllText(Path.Combine(native, "app.manifest"), "{\"name\":\"Sample native client\"}");
            return new Dictionary<string, string?> { ["webroot"] = root };
        }
    }

    #endregion

    #region Public Methods

    [Theory]
    [InlineData("/native/")]
    [InlineData("/native/extensions/edit")]
    [InlineData("/native/setup/model")]
    public async Task Native_deep_links_receive_the_native_shell_under_the_existing_CSP(string path)
    {
        var response = await NewClient().Get(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).Should().Contain("Sample native client");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("script-src 'self' 'wasm-unsafe-eval'").And.NotContain("'unsafe-eval'");
        response.Headers.GetValues("Cache-Control").Single().Should().Be("no-store, no-cache");
    }

    [Fact]
    public async Task The_existing_client_still_owns_the_root()
    {
        var response = await NewClient().Get("/");

        // The referenced web project's static assets own this route even with the native publish installed.
        (await response.Content.ReadAsStringAsync(Token)).Should().Contain("<title>Drover</title>")
            .And.Contain("_framework/blazor.webassembly.js").And.NotContain("Sample native client");
    }

    [Fact]
    public async Task Native_manifests_are_JSON_and_missing_assets_are_not_replaced_with_HTML()
    {
        var manifest = await NewClient().Get("/native/app.manifest");
        var missing = await NewClient().Get("/native/missing.js");
        var api = await NewClient().Get("/api/native/missing");

        manifest.StatusCode.Should().Be(HttpStatusCode.OK);
        manifest.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        api.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
