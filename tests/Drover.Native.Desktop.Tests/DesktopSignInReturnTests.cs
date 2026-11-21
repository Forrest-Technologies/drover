namespace Drover.Native.Desktop.Tests;

/// <summary>
/// The desktop app's door for a browser coming back from a tool server's sign-in page: a listener on this computer's
/// loopback address, reached here the way a browser reaches it.
/// </summary>
public sealed class DesktopSignInReturnTests
{
    #region Private Fields

    // The shape of a ticket the server makes: 32 bytes in URL-safe base64. Generated for the run.
    private static readonly string ticket = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_browser_coming_back_with_a_ticket_hands_it_to_the_app_and_is_told_to_go_back()
    {
        var arrived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = DesktopSignInReturn.Open(arrived.SetResult, NullLogger<DesktopSignInReturn>.Instance);

        var answer = await Visit(listener, $"/tool-sign-in?ticket={ticket}");

        listener.Address.Should().MatchRegex(@"^http://127\.0\.0\.1:\d+$", "the server takes only this exact form");
        answer.Should().StartWith("HTTP/1.1 200 OK\r\n");
        (await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Should().Be(ticket);
        answer.Should().Contain("Return to Drover").And.NotContain(ticket, "the page repeats nothing from the address");
        answer.Should().Contain("\r\nReferrer-Policy: no-referrer\r\n").And.Contain("\r\nContent-Security-Policy: default-src 'none'\r\n");
    }

    [Theory]
    [InlineData("/tool-sign-in")]
    [InlineData("/tool-sign-in?ticket=short")]
    [InlineData("/elsewhere?ticket=TICKET")]
    [InlineData("/tool-sign-in?ticket=TICKET%3Cscript")]
    public async Task Anything_else_is_not_found_and_hands_nothing_to_the_app(string path)
    {
        var handed = new List<string>();
        using var listener = DesktopSignInReturn.Open(handed.Add, NullLogger<DesktopSignInReturn>.Instance);

        var answer = await Visit(listener, path.Replace("TICKET", ticket, StringComparison.Ordinal));

        answer.Should().StartWith("HTTP/1.1 404 Not Found\r\n");
        handed.Should().BeEmpty();
    }

    [Theory]
    [InlineData("POST /tool-sign-in?ticket=TICKET HTTP/1.1", false)]
    [InlineData("GET /tool-sign-in?ticket=TICKET", false)]
    [InlineData("GET /tool-sign-in?other=1&ticket=TICKET HTTP/1.1", true)]
    [InlineData("GET /tool-sign-in?ticket=TICKET HTTP/1.1", true)]
    [InlineData("GET /tool-sign-in?ticket=TICKETX HTTP/1.1", false)]
    public void Only_a_get_of_the_return_path_with_a_well_formed_ticket_carries_one(string requestLine, bool carries)
    {
        var found = DesktopSignInReturn.TicketIn(requestLine.Replace("TICKET", ticket, StringComparison.Ordinal));

        found.Should().Be(carries ? ticket : null);
    }

    [Fact]
    public async Task A_connection_past_the_limit_is_refused_and_a_later_ticket_still_arrives()
    {
        var handed = new List<string>();
        using var listener = DesktopSignInReturn.Open(handed.Add, NullLogger<DesktopSignInReturn>.Instance);
        var port = new Uri(listener.Address).Port;
        var callers = new TcpClient[DesktopSignInReturn.MostCallers + 1];

        for (var index = 0; index < callers.Length; index++)
        {
            callers[index] = new TcpClient();
            await callers[index].ConnectAsync(IPAddress.Loopback, port, TestContext.Current.CancellationToken);
        }

        var answers = await Task.WhenAll(callers.Select(ReadOrGiveUp));

        try
        {
            answers.Count(answer => answer.StartsWith("HTTP/1.1 503 Service Unavailable\r\n", StringComparison.Ordinal)).Should().Be(1);
            handed.Should().BeEmpty();
        }
        finally
        {
            foreach (var caller in callers)
            {
                caller.Dispose();
            }
        }

        var returned = string.Empty;

        for (var attempt = 0; attempt < 20 && !returned.StartsWith("HTTP/1.1 200 OK\r\n", StringComparison.Ordinal); attempt++)
        {
            returned = await VisitIgnoringRefusal(listener, $"/tool-sign-in?ticket={ticket}");
        }

        returned.Should().StartWith("HTTP/1.1 200 OK\r\n");
        handed.Should().ContainSingle().Which.Should().Be(ticket);
    }

    [Fact]
    public void After_the_app_closes_nothing_listens()
    {
        var listener = DesktopSignInReturn.Open(_ => { }, NullLogger<DesktopSignInReturn>.Instance);
        var port = new Uri(listener.Address).Port;

        listener.Dispose();
        var connect = () =>
        {
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
        };

        connect.Should().Throw<SocketException>();
    }

    #endregion

    #region Private Methods

    /// <summary>The first bytes a held or refused connection sends, or empty when nothing arrives within two seconds.</summary>
    private static async Task<string> ReadOrGiveUp(TcpClient caller)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
        var buffer = new byte[256];

        try
        {
            var read = await caller.GetStream().ReadAsync(buffer.AsMemory(), linked.Token);
            return Encoding.ASCII.GetString(buffer, 0, read);
        }
        catch (OperationCanceledException cancelled) when (timeout.IsCancellationRequested)
        {
            return NothingArrived(cancelled);
        }
        catch (IOException failure)
        {
            return NothingArrived(failure);
        }
    }

    private static string NothingArrived(Exception failure) =>
        failure is OperationCanceledException or IOException ? string.Empty : throw failure;

    /// <summary>
    /// A connection past the limit is refused without its request being read, so closing it can send a reset instead
    /// of the 503 page on some systems. Neither shape carries a ticket; both leave the caller to try again.
    /// </summary>
    private static async Task<string> VisitIgnoringRefusal(DesktopSignInReturn listener, string path)
    {
        try
        {
            return await Visit(listener, path);
        }
        catch (IOException failure) when
            (failure.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset })
        {
            return NothingArrived(failure);
        }
    }

    /// <summary>What a browser sends to the listener and reads back, until the listener closes the connection.</summary>
    private static async Task<string> Visit(DesktopSignInReturn listener, string path)
    {
        var address = new Uri(listener.Address);
        using var browser = new TcpClient();
        await browser.ConnectAsync(IPAddress.Loopback, address.Port, TestContext.Current.CancellationToken);
        var stream = browser.GetStream();
        var request = $"GET {path} HTTP/1.1\r\nHost: {address.Authority}\r\nAccept: text/html\r\nUser-Agent: test\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    #endregion
}
