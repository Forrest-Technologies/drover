namespace Drover.Ui.Tests.Screens;

/// <summary>
/// The tool connections screen against a stand-in server: what each state says, what the form sends, and what a
/// person who is not an admin may do. Every name, address, and value here is a sample.
/// </summary>
public sealed class ToolConnectionsTests : ScreenTest
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid toolId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly string root = $"/api/spaces/{SpaceId}/tool-connections";

    #endregion

    #region List

    [Fact]
    public void Each_server_says_in_words_whether_it_answers_how_it_signs_in_and_what_went_wrong()
    {
        var failed = Connection("failed") with { Id = Guid.Parse("0196f1a0-0000-7000-8000-000000000005"), Name = "Quiet server", LastError = "Sample failure." };
        Listed([Connection("ready") with { LastListedAt = DateTimeOffset.UnixEpoch }, failed], Tool());
        Server.Answer("GET", $"{root}/{failed.Id}/tools", Array.Empty<ConnectionToolSummary>());

        var page = Render<ToolConnections>();

        page.WaitForAssertion(() => page.FindAll(".dv-connection-name").Select(name => name.TextContent).Should().Equal("Sample server", "Quiet server"));
        page.Find("h1").TextContent.Should().Be("Tool connections");
        page.FindAll(".dv-connection .dv-badge").Select(badge => badge.TextContent).Should().Equal("Connected", "Not connected");
        page.FindAll(".dv-connection-address").Select(address => address.TextContent).Should().AllBe("https://tools.example.test/mcp");
        page.FindAll(".dv-connection-text .dv-mono")[1].TextContent.Should().StartWith("no credential · 1 tool · listed ");
        page.FindAll(".dv-connection")[1].QuerySelector("[role=alert]")!.TextContent.Should().Contain("Sample failure.");
        page.FindAll(".dv-connection")[0].QuerySelectorAll("[role=alert] .dv-note").Should().BeEmpty();
        page.Find(".dv-connection-tool-title").TextContent.Should().Be("Look up");
    }

    [Fact]
    public void A_space_with_no_server_says_what_one_is_for_and_offers_to_add_it()
    {
        Listed([]);

        var page = Render<ToolConnections>();

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("No tool server yet"));
        page.Find(".dv-empty .dv-button").TextContent.Trim().Should().Be("Add a tool server");
    }

    [Fact]
    public void A_member_sees_the_servers_and_may_sign_in_for_themselves_and_change_nothing_else()
    {
        Server.Answer("GET", "/api/session", Person with { Spaces = [new(SpaceId, "personal", "Sam Sample", "sam", "member")] });
        var own = Connection("ready") with { AuthKind = "oauth", SignInScope = "per_user", SignIn = "needs_sign_in" };
        var shared = own with { Id = Guid.Parse("0196f1a0-0000-7000-8000-000000000005"), Name = "Shared server", SignInScope = "space" };
        Server.Answer("GET", root, new[] { own, shared });
        Server.Answer("GET", $"{root}/{own.Id}/tools", new[] { Tool() with { NeedsReview = true } });
        Server.Answer("GET", $"{root}/{shared.Id}/tools", Array.Empty<ConnectionToolSummary>());

        var page = Render<ToolConnections>();

        page.WaitForAssertion(() => page.FindAll(".dv-connection").Should().HaveCount(2));
        page.FindAll(".dv-connections-header .dv-button").Should().BeEmpty();
        page.FindAll(".dv-connection .dv-button").Select(button => button.TextContent.Trim()).Should().Equal(["Sign in"], "a member signs in for themselves and has no other control");
        page.FindAll(".dv-connection-need").Select(need => need.TextContent).Should().Equal(
            "You have not signed in to this server yet.",
            "Nobody has signed in to this server for the space yet.");
        page.FindAll(".dv-connection")[1].QuerySelector(".dv-connection-hint")!.TextContent.Should().Be("An owner or admin of the space signs in for everyone.");
    }

    [Fact]
    public void A_server_that_fails_to_load_says_so_and_offers_to_try_again()
    {
        SignedIn();
        Server.Answer("GET", root, HttpStatusCode.ServiceUnavailable);

        var page = Render<ToolConnections>();

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("Drover could not load the tool connections"));
    }

    #endregion

    #region Adding

    [Fact]
    public async Task The_form_asks_for_a_credential_only_when_one_is_chosen_and_sends_only_what_that_choice_uses()
    {
        Listed([]);
        Server.Answer("POST", root, new ToolConnectionRefresh(Connection("ready"), [Tool()], null, 1, 0, 0, 0), HttpStatusCode.Created);
        var page = Render<ToolConnections>();
        page.WaitForElement(".dv-empty .dv-button");
        await page.Find(".dv-empty .dv-button").ClickAsync(new MouseEventArgs());

        var plain = Labels(page);
        await Choose(page, 1);
        var header = Labels(page);
        await page.Find("input[type=password]").InputAsync(new ChangeEventArgs { Value = "sample-header-value" });
        await Choose(page, 2);
        var signIn = Labels(page);
        await page.Find(".dv-connect .dv-switch-input").ChangeAsync(new ChangeEventArgs { Value = true });
        var ownClient = Labels(page);
        await Type(page, "Name", "Sample server");
        await Type(page, "Address", "https://tools.example.test/mcp");
        await Type(page, "Client id", "sample-client");
        await Type(page, "Sign-in service address", "https://id.example.test");
        await page.Find(".dv-connect").SubmitAsync();

        plain.Should().Equal("Name", "Address", "Trust what this server's tools return", "Protocol");
        header.Should().Contain(["Header name", "Header value"]).And.NotContain("Client id");
        signIn.Should().Contain("Whose sign-in").And.NotContain("Client id").And.NotContain("Header value");
        ownClient.Should().Contain(["Client id", "Client secret", "Sign-in service address"]);
        page.WaitForAssertion(() => page.Find(".dv-connection-name").TextContent.Should().Be("Sample server"));
        var sent = Server.Sent("POST", root).Should().ContainSingle().Subject;
        sent.Should().Contain("\"authKind\":\"oauth\"").And.Contain("\"clientId\":\"sample-client\"").And.Contain("\"issuer\":\"https://id.example.test\"")
            .And.Contain("\"signInScope\":\"per_user\"").And.Contain("\"trust\":\"untrusted\"");
        sent.Should().NotContain("sample-header-value", "a value typed under a choice that was abandoned is not sent");
        page.FindAll(".dv-connect").Should().BeEmpty("the form closes, and what was typed into it is gone");
        page.Find(".dv-note-ok").TextContent.Should().Contain("Sample server is added.");
    }

    [Fact]
    public async Task A_refused_server_says_what_the_server_said_and_keeps_the_form()
    {
        Listed([]);
        Server.Refuse("POST", root, HttpStatusCode.BadRequest, "Enter the server's web address, starting with https:// or http://, with no user name or password in it.");
        var page = Render<ToolConnections>();
        page.WaitForElement(".dv-empty .dv-button");
        await page.Find(".dv-empty .dv-button").ClickAsync(new MouseEventArgs());

        await Type(page, "Name", "Sample server");
        await page.Find(".dv-connect").SubmitAsync();

        page.WaitForAssertion(() => page.Find(".dv-connect [role=alert]").TextContent.Should().Contain("Enter the server's web address"));
        page.Find(".dv-connect input").GetAttribute("value").Should().Be("Sample server");
    }

    #endregion

    #region Sign In

    [Fact]
    public async Task Signing_in_sends_the_browser_to_the_providers_page()
    {
        Listed([Connection("ready") with { AuthKind = "oauth", SignInScope = "per_user", SignIn = "needs_sign_in" }], Tool());
        Server.Answer("POST", $"{root}/{connectionId}/sign-in", new ToolSignInStart("redirect", "https://id.example.test/authorize?sample=1", null));
        var page = Render<ToolConnections>();
        page.WaitForElement(".dv-connection-ask .dv-button");

        await page.Find(".dv-connection-ask .dv-button").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Navigation.Uri.Should().Be("https://id.example.test/authorize?sample=1"));
    }

    [Fact]
    public async Task A_sign_in_that_cannot_start_says_why_on_its_server()
    {
        Listed([Connection("ready") with { AuthKind = "oauth", SignIn = "needs_reconnect" }], Tool());
        Server.Answer("POST", $"{root}/{connectionId}/sign-in", new ToolSignInStart("failed", null, "Sample reason the sign-in could not start."));
        var page = Render<ToolConnections>();
        page.WaitForElement(".dv-connection-ask .dv-button");

        page.Find(".dv-connection-need").TextContent.Should().Be("The space's sign-in to this server has lapsed. Sign in again to keep using its tools.");
        page.Find(".dv-connection-ask .dv-button").TextContent.Trim().Should().Be("Sign in again");
        await page.Find(".dv-connection-ask .dv-button").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find(".dv-connection [role=alert]").TextContent.Should().Contain("Sample reason the sign-in could not start."));
    }

    [Theory]
    [InlineData("connected", "You are signed in to Sample server.", "status")]
    [InlineData("failed", "The sign-in to Sample server did not finish. Nothing was stored. Try again.", "alert")]
    public void Coming_back_from_the_provider_says_how_it_went(string outcome, string words, string role)
    {
        Listed([Connection("ready") with { AuthKind = "oauth", SignIn = "signed_in" }], Tool());
        Navigation.NavigateTo(Addresses.ToolSignIn(outcome, connectionId.ToString()));

        var page = Render<ToolConnections>();

        page.WaitForAssertion(() => page.Find($".dv-note-region[role={role}] .dv-note").TextContent.Trim().Should().Be(words));
    }

    [Fact]
    public void The_start_page_hands_a_returning_sign_in_to_this_screen_and_passes_on_nothing_else()
    {
        SignedIn();
        Navigation.NavigateTo($"/?tool-sign-in=connected&space={SpaceId}&connection={connectionId}&extra=sample");

        Render<Threads>();

        Address.Should().Be($"tools?sign-in=connected&connection={connectionId}");
    }

    #endregion

    #region Review And Remove

    [Fact]
    public async Task A_changed_tool_shows_what_was_reviewed_beside_what_it_says_now_and_can_be_accepted()
    {
        var changed = Tool() with { Description = "Sample new words", NeedsReview = true, Reviewed = new ToolDefinitionSnapshot("Look up", "Sample old words", "{}") };
        Listed([Connection("ready")], changed);
        Server.Answer("POST", $"{root}/{connectionId}/tools/{toolId}/review", changed with { NeedsReview = false, Reviewed = null });
        var page = Render<ToolConnections>();
        page.WaitForElement(".dv-connection-review");

        page.Find(".dv-connection-tools").HasAttribute("open").Should().BeTrue("a server with something to review shows its tools");
        page.Find(".dv-connection-text .dv-mono:last-child").TextContent.Should().Contain("1 to review");
        page.FindAll(".dv-connection-compare dd").Select(words => words.TextContent).Should().Equal("Look upSample old words", "Look upSample new words");
        page.Find(".dv-connection-review .dv-button").TextContent.Trim().Should().Be("Accept the change to Look up");
        await page.Find(".dv-connection-review .dv-button").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.FindAll(".dv-connection-review").Should().BeEmpty());
        page.Find(".dv-note-ok").TextContent.Should().Contain("The change to Look up is accepted.");
    }

    [Fact]
    public async Task Removing_a_server_asks_first_and_says_what_goes_with_it()
    {
        Listed([Connection("ready")], Tool());
        Server.Answer("DELETE", $"{root}/{connectionId}", HttpStatusCode.NoContent);
        var page = Render<ToolConnections>();
        page.WaitForElement(".dv-connection .dv-button-danger");

        await page.Find(".dv-connection .dv-button-danger").ClickAsync(new MouseEventArgs());
        var asked = Server.Sent("DELETE", $"{root}/{connectionId}").Count();
        page.Find(".dv-dialog-title").TextContent.Should().Be("Remove Sample server");
        page.Find(".dv-dialog-body").TextContent.Should().Contain("Bots lose its tools at once");
        await page.Find(".dv-dialog-actions .dv-button-danger").ClickAsync(new MouseEventArgs());

        asked.Should().Be(0, "nothing is removed before the person confirms");
        page.WaitForAssertion(() => page.FindAll(".dv-connection").Should().BeEmpty());
        Server.Sent("DELETE", $"{root}/{connectionId}").Should().ContainSingle();
    }

    [Fact]
    public async Task A_header_is_replaced_without_ever_showing_the_old_one()
    {
        Listed([Connection("ready") with { AuthKind = "header" }], Tool());
        Server.Answer("PUT", $"{root}/{connectionId}/header", Connection("ready") with { AuthKind = "header" });
        var page = Render<ToolConnections>();
        page.WaitForElement(".dv-connection-actions .dv-button");

        page.Markup.Should().NotContain("type=\"password\"", "a stored credential has no field until it is being replaced");
        await page.FindAll(".dv-connection-actions .dv-button")[1].ClickAsync(new MouseEventArgs());
        var value = page.Find(".dv-connection-header input[type=password]");
        value.GetAttribute("value").Should().BeNullOrEmpty();
        await value.InputAsync(new ChangeEventArgs { Value = "sample-new-value" });
        await page.Find(".dv-connection-header .dv-button-primary").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.FindAll(".dv-connection-header").Should().BeEmpty());
        Server.Sent("PUT", $"{root}/{connectionId}/header").Should().ContainSingle().Which.Should().Contain("\"headerValue\":\"sample-new-value\"");
        page.Markup.Should().NotContain("sample-new-value");
    }

    #endregion

    #region Helpers

    private static ToolConnectionSummary Connection(string status) =>
        new(connectionId, "Sample server", "sample", "https://tools.example.test/mcp", "none", "untrusted", "automatic", status, null, null, null);

    private static ConnectionToolSummary Tool() =>
        new(toolId, connectionId, "lookup", "sample_lookup", "Look up", "Sample description", "write", "available", DateTimeOffset.UnixEpoch);

    private static List<string> Labels(IRenderedComponent<ToolConnections> page) =>
        [.. page.FindAll(".dv-connect label").Select(label => label.TextContent.Trim()).Where(label => label is not ("Nothing" or "A header" or "A sign-in"))];

    private static Task Choose(IRenderedComponent<ToolConnections> page, int kind) =>
        page.FindAll(".dv-connect .dv-segment-input")[kind].ChangeAsync(new ChangeEventArgs { Value = "on" });

    private static Task Type(IRenderedComponent<ToolConnections> page, string label, string text)
    {
        var id = page.FindAll(".dv-connect label").First(found => found.TextContent.Trim() == label).GetAttribute("for");
        return page.Find($"#{id}").InputAsync(new ChangeEventArgs { Value = text });
    }

    private void Listed(ToolConnectionSummary[] connections, params ConnectionToolSummary[] tools)
    {
        SignedIn();
        Server.Answer("GET", root, connections);

        if (connections.Length > 0)
        {
            Server.Answer("GET", $"{root}/{connections[0].Id}/tools", tools);
        }
    }

    #endregion
}
