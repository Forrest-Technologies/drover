namespace Drover.Server.Tests;

/// <summary>
/// The browser client, served at the root from the folder a deployment fills. Every address that is not a file is its
/// page; a missing file stays missing; files named after their content may be kept for good and nothing else may;
/// every answer carries the same security headers as the rest of the server; and the API is as it was. The address
/// the client was served under before, /native/, sends a browser to the same address at the root.
/// </summary>
public sealed class NativeClientTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string Page = "<!DOCTYPE html><title>Native test page</title>";

    private const string Package = "package_cf221fdf74c827459b26e43a2f22ee63c0c50e41";

    private const string Framework = "Drover.Native.vidq2wqwhe.wasm";

    #endregion

    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["NativeClient:Path"] = ClientFolder(),
    };

    #endregion

    #region Public Methods

    [Theory]
    [InlineData("/")]
    [InlineData("/sign-in")]
    [InlineData("/talk/01a1d987-ea5b-71c7-b0bf-e67eaaa8a61f")]
    [InlineData("/bots/01a1d987-ea5b-71c7-b0bf-e67eaaa8a61f/settings")]
    public async Task Every_address_of_the_client_is_its_page(string path)
    {
        var response = await NewClient().Get(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync(Token)).Should().Be(Page);
    }

    [Theory]
    [InlineData("/missing.js")]
    [InlineData("/_framework/Missing.abcdefgh12.wasm")]
    [InlineData($"/{Package}/missing.dll")]
    public async Task A_missing_file_is_not_answered_with_the_page(string path)
    {
        var response = await NewClient().Get(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/sign-in")]
    [InlineData("/boot.js")]
    [InlineData($"/_framework/{Framework}")]
    public async Task Every_answer_carries_the_same_security_policy_as_the_api(string path)
    {
        var client = await NewClient().Get(path);
        var api = await NewClient().Get("/api/setup");

        client.StatusCode.Should().Be(HttpStatusCode.OK);
        client.Headers.GetValues("Content-Security-Policy").Should().Equal(api.Headers.GetValues("Content-Security-Policy"));
        client.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("script-src 'self' 'wasm-unsafe-eval'")
            .And.NotContain("'unsafe-eval'");
        client.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        client.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
        client.Headers.GetValues("Referrer-Policy").Should().ContainSingle("no-referrer");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/sign-in")]
    [InlineData("/index.html")]
    [InlineData("/boot.js")]
    [InlineData("/service-worker.js")]
    [InlineData("/service-worker-assets.js")]
    [InlineData("/uno-config.js")]
    public async Task The_page_and_a_file_with_a_plain_name_are_asked_of_the_server_every_time(string path)
    {
        var response = await NewClient().Get(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Cache-Control").Single().Should().Be("no-store, no-cache");
    }

    [Theory]
    [InlineData($"/_framework/{Framework}")]
    [InlineData($"/{Package}/uno-bootstrap.js")]
    [InlineData($"/{Package}/Assets/Fonts/Inter.ttf.manifest")]
    public async Task A_file_named_after_its_content_may_be_kept_for_good(string path)
    {
        var response = await NewClient().Get(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Cache-Control").Single().Should().Be("public, max-age=31536000, immutable");
    }

    [Theory]
    [InlineData("/_framework/dotnet.js")]
    [InlineData("/package_/uno-bootstrap.js")]
    [InlineData("/package_notahash/uno-bootstrap.js")]
    [InlineData("/uno-config.js")]
    [InlineData("/native/package_cf221fdf74c827459b26e43a2f22ee63c0c50e41/uno-bootstrap.js")]
    [InlineData("/native/_framework/System.Linq.4jr7wme4t4.wasm")]
    public void Any_other_client_file_is_asked_of_the_server_every_time(string path) =>
        ClientFileCaching.For(path).Should().Be("no-cache, no-store");

    [Fact]
    public async Task A_font_manifest_is_served_as_json()
    {
        var response = await NewClient().Get($"/{Package}/Assets/Fonts/Inter.ttf.manifest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task A_browser_that_accepts_brotli_gets_the_compressed_copy_with_the_files_own_type()
    {
        var client = NewClient();
        client.ForwardedHeaders["Accept-Encoding"] = "gzip, deflate, br";

        var response = await client.Get($"/_framework/{Framework}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.Should().Equal("br");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/wasm");
        response.Headers.Vary.Should().Contain("Accept-Encoding");
        response.Headers.GetValues("Cache-Control").Single().Should().Be("public, max-age=31536000, immutable");
        await using var body = await response.Content.ReadAsStreamAsync(Token);
        await using var plain = new BrotliStream(body, CompressionMode.Decompress);
        using var reader = new StreamReader(plain);
        (await reader.ReadToEndAsync(Token)).Should().Be("wasm bytes");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("gzip;q=0, br;q=0")]
    [InlineData("identity")]
    public async Task A_browser_that_does_not_accept_the_encoding_gets_the_plain_file(string? acceptEncoding)
    {
        var client = NewClient();
        if (acceptEncoding is not null)
        {
            client.ForwardedHeaders["Accept-Encoding"] = acceptEncoding;
        }

        var response = await client.Get($"/_framework/{Framework}");

        response.Content.Headers.ContentEncoding.Should().BeEmpty();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/wasm");
        (await response.Content.ReadAsStringAsync(Token)).Should().Be("wasm bytes");
    }

    [Fact]
    public async Task The_api_is_never_answered_with_the_page()
    {
        var setup = await NewClient().Get("/api/setup");
        var missing = await NewClient().Get("/api/native/sign-in");

        setup.StatusCode.Should().Be(HttpStatusCode.OK);
        setup.Headers.GetValues("Cache-Control").Single().Should().Be("no-store");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync(Token)).Should().NotBe(Page);
    }

    [Theory]
    [InlineData("/native", "/")]
    [InlineData("/native/", "/")]
    [InlineData("/native/sign-in", "/sign-in")]
    [InlineData("/native/talk/01a1d987-ea5b-71c7-b0bf-e67eaaa8a61f/memory", "/talk/01a1d987-ea5b-71c7-b0bf-e67eaaa8a61f/memory")]
    [InlineData("/native?tool-sign-in=connected&connection=sample", "/?tool-sign-in=connected&connection=sample")]
    [InlineData("/native/?tool-sign-in=connected&connection=sample", "/?tool-sign-in=connected&connection=sample")]
    [InlineData("/native/manifest.webmanifest", "/manifest.webmanifest")]
    [InlineData("/NATIVE/sign-in", "/sign-in")]
    public async Task The_address_the_client_was_served_under_sends_the_browser_to_the_same_address_at_the_root(
        string path, string location)
    {
        var response = await NewClient().Get(path);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.IsAbsoluteUri.Should().BeFalse();
        response.Headers.Location.OriginalString.Should().Be(location);
    }

    [Theory]
    [InlineData("/native//elsewhere.example/sign-in")]
    [InlineData("/native//elsewhere.example/sign-in?from=link")]
    [InlineData("/native/%5Celsewhere.example")]
    [InlineData("/native/%5C%5Celsewhere.example")]
    [InlineData("/native/%2F%2Felsewhere.example")]
    [InlineData("/native/%2F%5Celsewhere.example?next=x")]
    [InlineData("/native/https:%2F%2Felsewhere.example")]
    [InlineData("/native/elsewhere.example%40drover.example")]
    [InlineData("/native/%09%2F%2Felsewhere.example")]
    [InlineData("/native/a%20b")]
    [InlineData("/native/sign-in/")]
    public async Task An_address_that_is_not_plain_path_segments_is_sent_to_the_root_alone(string path)
    {
        var response = await NewClient().Get(path);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/");
    }

    [Theory]
    [InlineData("/native/sign-in?next=//elsewhere.example", "/sign-in?next=//elsewhere.example")]
    [InlineData("/native?next=%2F%2Felsewhere.example", "/?next=%2F%2Felsewhere.example")]
    [InlineData("/native/talk/0196f1a0-0000-7000-8000-000000000004/side/new", "/talk/0196f1a0-0000-7000-8000-000000000004/side/new")]
    public async Task A_query_can_never_take_the_browser_to_another_host(string path, string location)
    {
        var response = await NewClient().Get(path);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(location);
        new Uri(new Uri("https://drover.example/"), response.Headers.Location).Host.Should().Be("drover.example");
    }

    [Fact]
    public async Task The_retired_clients_worker_is_answered_with_one_that_removes_it()
    {
        var response = await NewClient().Get("/native/service-worker.js");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/javascript");
        response.Headers.GetValues("Cache-Control").Single().Should().Be("no-store, no-cache");
        response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle();
        var script = await response.Content.ReadAsStringAsync(Token);
        script.Should().Contain("self.registration.unregister()").And.Contain("unsubscribe()").And.NotContain("\"fetch\"");
    }

    #endregion

    #region Private Methods

    // Inside the key ring folder, which the base class removes after the test.
    private string ClientFolder()
    {
        var root = Path.Combine(KeyRingPath, "client");
        Write(root, "index.html", Page);
        Write(root, "boot.js", "// boot");
        Write(root, "service-worker.js", "// worker");
        Write(root, "service-worker-assets.js", "self.assetsManifest = {};");
        Write(root, "uno-config.js", "export {};");
        Write(root, $"_framework/{Framework}", "wasm bytes");
        File.WriteAllBytes(Path.Combine(root, "_framework", $"{Framework}.br"), Brotli("wasm bytes"));
        Write(root, $"{Package}/uno-bootstrap.js", "// bootstrap");
        Write(root, $"{Package}/Assets/Fonts/Inter.ttf.manifest", """{ "fonts": [] }""");
        return root;
    }

    private static void Write(string root, string name, string text)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static byte[] Brotli(string text)
    {
        using var compressed = new MemoryStream();
        using (var brotli = new BrotliStream(compressed, CompressionLevel.Optimal))
        {
            brotli.Write(Encoding.UTF8.GetBytes(text));
        }

        return compressed.ToArray();
    }

    #endregion
}


/// <summary>
/// A publish copied into the server's web root, as the image does, is found there with no setting and owns the root.
/// </summary>
public sealed class NativeClientInWebRootTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings
    {
        get
        {
            var root = Path.Combine(KeyRingPath, "client");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "index.html"), "<!DOCTYPE html><title>Sample client</title>");
            File.WriteAllText(Path.Combine(root, "app.manifest"), "{\"name\":\"Sample client\"}");
            return new Dictionary<string, string?> { ["webroot"] = root };
        }
    }

    #endregion

    #region Public Methods

    [Theory]
    [InlineData("/")]
    [InlineData("/extensions/edit")]
    [InlineData("/setup/model")]
    public async Task Every_address_of_the_client_receives_its_page_under_the_existing_CSP(string path)
    {
        var response = await NewClient().Get(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Token)).Should().Contain("Sample client");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("script-src 'self' 'wasm-unsafe-eval'").And.NotContain("'unsafe-eval'");
        response.Headers.GetValues("Cache-Control").Single().Should().Be("no-store, no-cache");
    }

    [Fact]
    public async Task A_file_of_the_client_is_served_from_the_web_root()
    {
        var response = await NewClient().Get("/app.manifest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    #endregion
}
