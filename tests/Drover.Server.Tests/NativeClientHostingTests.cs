namespace Drover.Server.Tests;

/// <summary>
/// The native client's browser build, served beside the web client under /native/ from the folder a deployment
/// fills. Every address that is not a file is its page; a missing file stays missing; files named after their
/// content may be kept for good and nothing else may; every answer carries the same security headers as the rest of
/// the server; and the API and the web client are as they were.
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
        ["NativeClient:Path"] = NativeFolder(),
    };

    #endregion

    #region Public Methods

    [Theory]
    [InlineData("/native/")]
    [InlineData("/native/sign-in")]
    [InlineData("/native/talk/01a1d987-ea5b-71c7-b0bf-e67eaaa8a61f")]
    [InlineData("/native/bots/01a1d987-ea5b-71c7-b0bf-e67eaaa8a61f/settings")]
    public async Task Every_address_of_the_native_client_is_its_page(string path)
    {
        var response = await NewClient().Get(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync(Token)).Should().Be(Page);
    }

    [Fact]
    public async Task The_native_folder_without_its_slash_is_sent_to_it()
    {
        var response = await NewClient().Get("/native");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/native/");
    }

    [Theory]
    [InlineData("/native/missing.js")]
    [InlineData("/native/_framework/Missing.abcdefgh12.wasm")]
    [InlineData($"/native/{Package}/missing.dll")]
    public async Task A_missing_file_is_not_answered_with_the_page(string path)
    {
        var response = await NewClient().Get(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/native/sign-in")]
    [InlineData("/native/boot.js")]
    [InlineData($"/native/_framework/{Framework}")]
    public async Task Every_answer_carries_the_same_security_policy_as_the_web_client(string path)
    {
        var native = await NewClient().Get(path);
        var web = await NewClient().Get("/");

        native.StatusCode.Should().Be(HttpStatusCode.OK);
        native.Headers.GetValues("Content-Security-Policy").Should().Equal(web.Headers.GetValues("Content-Security-Policy"));
        native.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        native.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
        native.Headers.GetValues("Referrer-Policy").Should().ContainSingle("no-referrer");
    }

    [Theory]
    [InlineData("/native/")]
    [InlineData("/native/sign-in")]
    [InlineData("/native/index.html")]
    [InlineData("/native/boot.js")]
    [InlineData("/native/service-worker.js")]
    [InlineData("/native/service-worker-assets.js")]
    [InlineData("/native/uno-config.js")]
    public async Task The_page_and_a_file_with_a_plain_name_are_asked_of_the_server_every_time(string path)
    {
        var response = await NewClient().Get(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Cache-Control").Single().Should().Be("no-store, no-cache");
    }

    [Theory]
    [InlineData($"/native/_framework/{Framework}")]
    [InlineData($"/native/{Package}/uno-bootstrap.js")]
    [InlineData($"/native/{Package}/Assets/Fonts/Inter.ttf.manifest")]
    public async Task A_file_named_after_its_content_may_be_kept_for_good(string path)
    {
        var response = await NewClient().Get(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Cache-Control").Single().Should().Be("public, max-age=31536000, immutable");
    }

    [Theory]
    [InlineData("/native/_framework/dotnet.js")]
    [InlineData("/native/package_/uno-bootstrap.js")]
    [InlineData("/native/package_notahash/uno-bootstrap.js")]
    [InlineData("/native/uno-config.js")]
    [InlineData("/package_cf221fdf74c827459b26e43a2f22ee63c0c50e41/uno-bootstrap.js")]
    public void Any_other_native_file_is_asked_of_the_server_every_time(string path) =>
        ClientFileCaching.For(path).Should().Be("no-cache, no-store");

    [Fact]
    public async Task A_font_manifest_is_served_as_json()
    {
        var response = await NewClient().Get($"/native/{Package}/Assets/Fonts/Inter.ttf.manifest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task A_browser_that_accepts_brotli_gets_the_compressed_copy_with_the_files_own_type()
    {
        var client = NewClient();
        client.ForwardedHeaders["Accept-Encoding"] = "gzip, deflate, br";

        var response = await client.Get($"/native/_framework/{Framework}");

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

        var response = await client.Get($"/native/_framework/{Framework}");

        response.Content.Headers.ContentEncoding.Should().BeEmpty();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/wasm");
        (await response.Content.ReadAsStringAsync(Token)).Should().Be("wasm bytes");
    }

    [Fact]
    public async Task The_api_and_the_web_client_are_unaffected()
    {
        var setup = await NewClient().Get("/api/setup");
        var missing = await NewClient().Get("/api/native/sign-in");
        var web = await NewClient().Get("/sign-in");

        setup.StatusCode.Should().Be(HttpStatusCode.OK);
        setup.Headers.GetValues("Cache-Control").Single().Should().Be("no-store");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        web.StatusCode.Should().Be(HttpStatusCode.OK);
        (await web.Content.ReadAsStringAsync(Token)).Should().NotBe(Page);
    }

    #endregion

    #region Private Methods

    // Inside the key ring folder, which the base class removes after the test.
    private string NativeFolder()
    {
        var root = Path.Combine(KeyRingPath, "native-client");
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
/// A native publish copied into the server's web root, as the image does, is found there with no setting, and the
/// web client keeps the root.
/// </summary>
public sealed class NativeClientInWebRootTests(PostgresServer postgres) : ServerTestBase(postgres)
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

    #endregion
}
