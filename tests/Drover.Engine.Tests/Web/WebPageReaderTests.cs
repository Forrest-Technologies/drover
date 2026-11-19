namespace Drover.Engine.Tests.Web;

/// <summary>
/// <c>web.fetch</c>'s reader against a local web server: a page is read as its readable text, and an address a model
/// chose never reaches the server's own network, whatever the deployment allows, including after a redirect and after
/// the host name resolves. The real address check is used wherever the address is a literal or resolves for real.
/// </summary>
public sealed class WebPageReaderTests
{
    #region Private Fields

    private const string Page =
        """
        <!doctype html>
        <html>
          <head><title>Drover field notes</title><style>body { color: red; }</style></head>
          <body>
            <nav><a href="/">Home</a> <a href="/about">About</a></nav>
            <script>window.secret = "never read";</script>
            <main>
              <h1>Field notes</h1>
              <p>The barn   was <b>full</b> of hay.</p>
              <ul><li>Oats</li><li>Barley</li></ul>
            </main>
            <footer>Copyright footer</footer>
          </body>
        </html>
        """;

    #endregion

    #region Private Addresses

    [Fact]
    public async Task A_loopback_address_is_refused_even_where_the_deployment_allows_private_addresses()
    {
        using var server = new TestWebServer(_ => Html(Page));
        var reader = Reader(DeploymentPolicy.SelfHosted);

        var byAddress = () => reader.Read(new Uri($"http://127.0.0.1:{server.Port}/"), Token);
        var byName = () => reader.Read(new Uri($"http://localhost:{server.Port}/"), Token);

        (await byAddress.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain("private, loopback, or link-local");
        (await byName.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain("localhost");
        server.Requests.Should().Be(0, "nothing is sent to a refused address");
    }

    [Theory]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://172.16.4.2/")]
    [InlineData("http://192.168.1.1/admin")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://100.64.0.1/")]
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    public async Task Private_link_local_and_metadata_addresses_are_refused(string address)
    {
        var reader = Reader(DeploymentPolicy.SelfHosted);

        var read = () => reader.Read(new Uri(address), Token);

        (await read.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain("Web pages are read only from public addresses");
    }

    [Fact]
    public async Task A_host_name_that_resolves_to_a_private_address_is_refused()
    {
        var reader = Reader(DeploymentPolicy.SelfHosted, _ => [IPAddress.Parse("10.1.2.3")], PrivateAddresses.Contains);

        var read = () => reader.Read(new Uri("http://looks-public.test/"), Token);

        (await read.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain("looks-public.test");
    }

    [Fact]
    public async Task A_redirect_to_a_metadata_address_or_to_a_name_that_resolves_privately_is_refused()
    {
        using var toMetadata = new TestWebServer(_ => TestWebServer.Redirect("http://169.254.169.254/latest/meta-data/"));
        using var toInside = new TestWebServer(_ => TestWebServer.Redirect("http://inside.test/"));
        var reader = Reader(DeploymentPolicy.SelfHosted, PublicOrInside, OnlyTheTestServerIsPublic);

        var first = () => reader.Read(new Uri($"http://public.test:{toMetadata.Port}/"), Token);
        var second = () => reader.Read(new Uri($"http://public.test:{toInside.Port}/"), Token);

        (await first.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain("169.254.169.254");
        (await second.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain("inside.test");
        toMetadata.Requests.Should().Be(1);
        toInside.Requests.Should().Be(1);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://files.example/readme.txt")]
    [InlineData("https://name:password@example.com/")]
    public async Task Only_plain_http_and_https_addresses_are_read(string address)
    {
        var reader = Reader(DeploymentPolicy.SelfHosted);

        var read = () => reader.Read(new Uri(address), Token);

        await read.Should().ThrowAsync<BuiltinToolException>();
    }

    #endregion

    #region Reading

    [Fact]
    public async Task A_page_is_read_as_its_title_and_readable_text_without_scripts_styles_or_navigation()
    {
        using var server = new TestWebServer(_ => Html(Page));
        var reader = Reader(DeploymentPolicy.SelfHosted, PublicOrInside, OnlyTheTestServerIsPublic);

        var page = await reader.Read(new Uri($"http://public.test:{server.Port}/notes"), Token);

        page.Title.Should().Be("Drover field notes");
        page.Text.Should().Be("Field notes\nThe barn was full of hay.\n- Oats\n- Barley");
        page.IsCut.Should().BeFalse();
        page.Url.AbsolutePath.Should().Be("/notes");
    }

    [Fact]
    public async Task A_redirect_to_a_public_page_is_followed_and_the_page_says_where_it_ended()
    {
        using var server = new TestWebServer(request => request.StartsWith("GET /old ", StringComparison.Ordinal)
            ? TestWebServer.Redirect("/new")
            : Text("text/plain", "Moved here."));
        var reader = Reader(DeploymentPolicy.SelfHosted, PublicOrInside, OnlyTheTestServerIsPublic);

        var page = await reader.Read(new Uri($"http://public.test:{server.Port}/old"), Token);

        page.Text.Should().Be("Moved here.");
        page.Url.AbsolutePath.Should().Be("/new");
    }

    [Fact]
    public async Task A_page_that_keeps_redirecting_is_given_up_after_five_redirects()
    {
        using var server = new TestWebServer(_ => TestWebServer.Redirect("/again"));
        var reader = Reader(DeploymentPolicy.SelfHosted, PublicOrInside, OnlyTheTestServerIsPublic);

        var read = () => reader.Read(new Uri($"http://public.test:{server.Port}/"), Token);

        (await read.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain($"more than {WebPageReader.MaxRedirects} times");
        server.Requests.Should().Be(WebPageReader.MaxRedirects + 1);
    }

    [Fact]
    public async Task A_page_larger_than_the_policy_allows_is_cut_and_says_so()
    {
        using var server = new TestWebServer(_ => Text("text/plain", new string('a', 5000)));
        var reader = Reader(DeploymentPolicy.SelfHosted with { MaxWebPageBytes = 1000 }, PublicOrInside, OnlyTheTestServerIsPublic);

        var page = await reader.Read(new Uri($"http://public.test:{server.Port}/"), Token);

        page.IsCut.Should().BeTrue();
        page.Text.Should().HaveLength(1000);
    }

    [Fact]
    public async Task A_page_that_is_not_text_is_refused()
    {
        using var server = new TestWebServer(_ => Text("image/png", "PNG"));
        var reader = Reader(DeploymentPolicy.SelfHosted, PublicOrInside, OnlyTheTestServerIsPublic);

        var read = () => reader.Read(new Uri($"http://public.test:{server.Port}/picture"), Token);

        (await read.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain("image/png, not text");
    }

    [Fact]
    public async Task A_page_that_answers_with_an_error_status_is_refused_with_the_status()
    {
        using var server = new TestWebServer(_ => Text("text/html", "Not here", "404 Not Found"));
        var reader = Reader(DeploymentPolicy.SelfHosted, PublicOrInside, OnlyTheTestServerIsPublic);

        var read = () => reader.Read(new Uri($"http://public.test:{server.Port}/missing"), Token);

        (await read.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain("status 404");
    }

    [Fact]
    public async Task A_page_slower_than_the_policy_allows_is_given_up()
    {
        using var server = new TestWebServer(_ =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(3));
            return Html(Page);
        });
        var reader = Reader(DeploymentPolicy.SelfHosted with { MaxWebRequestSeconds = 1 }, PublicOrInside, OnlyTheTestServerIsPublic);

        var read = () => reader.Read(new Uri($"http://public.test:{server.Port}/"), Token);

        (await read.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain("longer than 1 seconds");
    }

    [Fact]
    public void A_page_nested_thousands_of_levels_deep_is_read_without_exhausting_the_stack()
    {
        var html = $"{string.Concat(Enumerable.Repeat("<div>", 50_000))}deep{string.Concat(Enumerable.Repeat("</div>", 50_000))}";

        var (_, text) = ReadableText.FromHtml(html);

        text.Should().Be("deep");
    }

    #endregion

    #region Private Methods

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// A reader as the server makes one: the checked handler without redirects, and no request stating a space's rule.
    /// With <paramref name="resolve"/>, host names resolve as the test says and <paramref name="isRefused"/> decides.
    /// </summary>
    private static WebPageReader Reader(DeploymentPolicy policy, Func<string, IPAddress[]>? resolve = null, Func<IPAddress, bool>? isRefused = null)
    {
        var handler = resolve is null
            ? OutboundHttp.CreateHandlerWithoutRedirects(policy)
            : OutboundHttp.CreateHandler(policy, (host, _) => Task.FromResult(resolve(host)), isRefused ?? PrivateAddresses.Contains, followRedirects: false);
        return new WebPageReader(
            PageClients.Through(handler),
            Options.Create(new DeploymentPolicyOptions { Rules = policy }),
            NullLogger<WebPageReader>.Instance);
    }

    /// <summary><c>public.test</c> is the local test server; <c>inside.test</c> is another loopback address.</summary>
    private static IPAddress[] PublicOrInside(string host) =>
        host == "public.test" ? [IPAddress.Loopback] : [IPAddress.Parse("127.0.0.2")];

    /// <summary>Only the test server's own address passes, so every other address counts as private.</summary>
    private static bool OnlyTheTestServerIsPublic(IPAddress address) => !address.Equals(IPAddress.Loopback);

    private static string Html(string body) => Text("text/html; charset=utf-8", body);

    private static string Text(string contentType, string body, string status = "200 OK") =>
        $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";

    #endregion
}
