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
        answer.Should().StartWith("HTTP/1.1 200 OK\r\n").And.Contain("Signed in. You can close this tab.");
        answer.Should().NotContain("sample", "the page repeats nothing from the address");
        (await listening.Returned.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Should().Be("code=sample&state=sample");
    }

    [Theory]
    [InlineData("/elsewhere?code=sample")]
    [InlineData("/auth/callback")]
    [InlineData("/auth/callbackx?code=sample")]
    [InlineData("/tool-sign-in?ticket=sample")]
    public async Task Any_other_path_is_not_found_and_hands_nothing_on(string path)
    {
        using var listening = new DesktopModelSignInListener(NullLogger<DesktopModelSignInListener>.Instance).Open();

        var answer = await Visit(listening.RedirectUri, path);

        answer.Should().StartWith("HTTP/1.1 404 Not Found\r\n");
        listening.Returned.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void A_second_listener_takes_another_port_while_the_first_holds_its_own()
    {
        var listener = new DesktopModelSignInListener(NullLogger<DesktopModelSignInListener>.Instance);
        using var first = listener.Open();
        using var second = listener.Open();

        second.RedirectUri.Port.Should().NotBe(first.RedirectUri.Port);
    }

    [Theory]
    [InlineData("GET /auth/callback?code=a HTTP/1.1", "code=a")]
    [InlineData("POST /auth/callback?code=a HTTP/1.1", null)]
    [InlineData("GET /auth/callback? HTTP/1.1", null)]
    [InlineData("GET /auth/callback?code=a", null)]
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
