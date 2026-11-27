namespace Drover.Native.Desktop.Tests;

/// <summary>
/// The desktop app's listener for a browser coming back from a model provider's sign-in page, reached here the way a
/// browser reaches it: one return on the provider's path is handed on, and any other path is not found.
/// </summary>
public sealed class DesktopModelSignInListenerTests
{
    #region Public Methods

    [Fact]
    public async Task A_return_on_the_path_is_handed_on_once_and_the_browser_is_told_it_can_close_the_tab()
    {
        using var listening = new DesktopModelSignInListener(NullLogger<DesktopModelSignInListener>.Instance).Open();

        var answer = await Visit(listening.RedirectUri, "/auth/callback?code=sample&state=sample");

        listening.RedirectUri.ToString().Should().MatchRegex(@"^http://127\.0\.0\.1:\d+/auth/callback$", "the provider takes only this form, on any port");
        answer.Should().StartWith("HTTP/1.1 200 OK\r\n").And.Contain("You can close this tab.").And.NotContain("Signed in", "the server has not finished yet");
        answer.Should().NotContain("sample", "the page repeats nothing from the address");
        (await listening.Returned.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Should().Be("code=sample&state=sample");
    }

    [Theory]
    [InlineData("/elsewhere?code=sample")]
    [InlineData("/auth/callback")]
    [InlineData("/auth/callbackx?code=sample")]
    [InlineData("/tool-sign-in?ticket=sample")]
    [InlineData("/auth/callback?code=sample")]
    [InlineData("/auth/callback?favicon=1")]
    public async Task Any_other_path_is_not_found_and_hands_nothing_on(string path)
    {
        using var listening = new DesktopModelSignInListener(NullLogger<DesktopModelSignInListener>.Instance).Open();

        var answer = await Visit(listening.RedirectUri, path);

        answer.Should().StartWith("HTTP/1.1 404 Not Found\r\n");
        listening.Returned.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task A_stray_visit_is_not_found_and_the_listener_keeps_waiting_for_the_return()
    {
        using var listening = new DesktopModelSignInListener(NullLogger<DesktopModelSignInListener>.Instance).Open();

        var stray = await Visit(listening.RedirectUri, "/auth/callback?favicon=1");
        var answer = await Visit(listening.RedirectUri, "/auth/callback?code=c&state=s");

        stray.Should().StartWith("HTTP/1.1 404 Not Found\r\n");
        answer.Should().StartWith("HTTP/1.1 200 OK\r\n");
        (await listening.Returned.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Should().Be("code=c&state=s");
    }

    [Fact]
    public void A_second_listener_takes_another_port_while_the_first_holds_its_own()
    {
        var listener = new DesktopModelSignInListener(NullLogger<DesktopModelSignInListener>.Instance);
        using var first = listener.Open();
        using var second = listener.Open();

        second.RedirectUri.Port.Should().NotBe(first.RedirectUri.Port);
    }

    [Fact]
    public async Task The_listener_keeps_the_host_and_path_the_kind_names_and_takes_a_return_only_there()
    {
        using var listening = new DesktopModelSignInListener(NullLogger<DesktopModelSignInListener>.Instance).Open(new Uri("http://localhost:0/callback"));

        var elsewhere = await Visit(listening.RedirectUri, "/auth/callback?code=c&state=s");
        var answer = await Visit(listening.RedirectUri, "/callback?code=c&state=s");

        listening.RedirectUri.ToString().Should().MatchRegex(@"^http://localhost:\d+/callback$");
        elsewhere.Should().StartWith("HTTP/1.1 404 Not Found\r\n", "another kind's path is not this one's");
        answer.Should().StartWith("HTTP/1.1 200 OK\r\n");
        (await listening.Returned.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Should().Be("code=c&state=s");
    }

    [Theory]
    [InlineData("https://127.0.0.1:1455/callback")]
    [InlineData("http://example.test:1455/callback")]
    [InlineData("http://192.168.1.10:1455/callback")]
    [InlineData("http://127.0.0.1:1455/callback?next=1")]
    [InlineData("http://user@127.0.0.1:1455/callback")]
    public void A_return_address_that_is_not_this_computers_loopback_is_not_listened_for(string address)
    {
        var opening = () => new DesktopModelSignInListener(NullLogger<DesktopModelSignInListener>.Instance).Open(new Uri(address));

        opening.Should().Throw<DeviceCallFailedException>();
    }

    [Theory]
    [InlineData("GET /auth/callback?code=a&state=s HTTP/1.1", "code=a&state=s")]
    [InlineData("GET /auth/callback?error=access_denied&state=s HTTP/1.1", "error=access_denied&state=s")]
    [InlineData("GET /auth/callback?code=a HTTP/1.1", null)]
    [InlineData("POST /auth/callback?code=a&state=s HTTP/1.1", null)]
    [InlineData("GET /auth/callback? HTTP/1.1", null)]
    [InlineData("GET /auth/callback?code=a&state=s", null)]
    public void Only_a_get_of_the_return_path_with_a_query_carries_one(string requestLine, string? query)
    {
        DesktopModelSignInListener.QueryIn(requestLine).Should().Be(query);
    }

    #endregion

    #region Private Methods

    private static async Task<string> Visit(Uri listening, string path)
    {
        using var browser = new TcpClient();
        await browser.ConnectAsync(IPAddress.Loopback, listening.Port, TestContext.Current.CancellationToken);
        var stream = browser.GetStream();
        var request = $"GET {path} HTTP/1.1\r\nHost: {listening.Authority}\r\nAccept: text/html\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    #endregion
}
