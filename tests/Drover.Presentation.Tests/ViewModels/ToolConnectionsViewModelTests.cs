namespace Drover.Presentation.Tests.ViewModels;

/// <summary>Tool-server actions preserve the existing contracts, one-time return handling, and review conflicts.</summary>
public sealed class ToolConnectionsViewModelTests : IDisposable
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid toolId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly string space = $"/api/spaces/{Samples.Session().Spaces[0].Id}";

    private static readonly string path = $"{space}/tool-connections";

    private readonly FakeServer server = new();

    private readonly Navigator navigator = new();

    #endregion

    #region Constructors

    public ToolConnectionsViewModelTests()
    {
        server.Answer("GET", "/api/session", Samples.Session());
        server.Answer("GET", path, new[] { Connection });
        server.Answer("GET", $"{path}/{connectionId}/tools", new[] { Tool });
    }

    #endregion

    #region Properties

    private static Drover.Contracts.Tools.ToolConnectionSummary Connection =>
        new(connectionId, "Sample tools", "sample-tools", "https://tools.example.test/mcp", "oauth", "untrusted", "automatic", "ready", null, null, null);

    private static Drover.Contracts.Tools.ConnectionToolSummary Tool =>
        new(toolId, connectionId, "sample", "sample__sample", "Sample tool", "Sample description", "read", "available", DateTimeOffset.UnixEpoch)
        {
            DefinitionHash = "sample-shown-definition",
            NeedsReview = true,
        };

    #endregion

    #region Public Methods

    public void Dispose() => server.Dispose();

    [Fact]
    public async Task Replacing_a_header_opens_for_one_connection_and_cancel_clears_the_value()
    {
        var model = Model();
        await model.Open();

        model.StartReplacingHeader(Connection);
        model.HeaderName = "X-Additional";
        model.HeaderValue = "sample-header-value";

        model.ReplacingHeader.Should().Be(Connection);
        model.HeaderName.Should().Be("X-Additional");
        model.CancelReplacingHeader();
        model.ReplacingHeader.Should().BeNull();
        model.HeaderValue.Should().BeEmpty();
    }

    [Fact]
    public async Task A_saved_header_closes_the_dialog_and_a_refused_one_keeps_it_open_with_the_reason()
    {
        server.Answer("PUT", $"{path}/{connectionId}/header", Connection);
        var model = Model();
        await model.Open();
        model.StartReplacingHeader(Connection);
        model.HeaderValue = " sample-header-value ";

        await model.ReplaceHeader(Connection);

        model.ReplacingHeader.Should().BeNull();
        model.HeaderValue.Should().BeEmpty();
        model.Notice.Should().Be("The header for Sample tools is replaced.");
        JsonSerializer.Deserialize<ReplaceToolHeaderRequest>(
            server.Sent("PUT", $"{path}/{connectionId}/header").Single(), JsonSerializerOptions.Web)
            .Should().Be(new ReplaceToolHeaderRequest("Authorization", " sample-header-value "));

        server.Refuse("PUT", $"{path}/{connectionId}/header", HttpStatusCode.BadRequest, "Sample header refused.");
        model.StartReplacingHeader(Connection);
        model.HeaderValue = "another-value";

        await model.ReplaceHeader(Connection);

        model.ReplacingHeader.Should().Be(Connection);
        model.HeaderValue.Should().Be("another-value");
        model.HeaderProblem.Should().Be("Sample header refused.");
        model.Problems.Should().NotContainKey(connectionId, "a dialog refusal is not a card error");
        model.ChangePending.Should().BeFalse();
    }

    [Fact]
    public async Task A_member_can_read_the_catalog_but_is_not_offered_admin_controls()
    {
        server.Answer("GET", "/api/session", Samples.Session("member"));
        var model = Model();

        await model.Open();

        model.CanEdit.Should().BeFalse();
        model.Connections.Should().ContainSingle();
        model.Tools[connectionId].Should().ContainSingle().Which.Name.Should().Be("sample");
    }

    [Fact]
    public async Task A_changed_definition_is_accepted_only_with_the_hash_the_person_was_shown()
    {
        var accepted = Tool with { NeedsReview = false };
        server.Answer("POST", $"{path}/{connectionId}/tools/{toolId}/review", accepted);
        var model = Model();
        await model.Open();

        await model.Accept(Tool);

        var sent = JsonSerializer.Deserialize<Drover.Contracts.Tools.ConfirmToolReviewRequest>(
            server.Sent("POST", $"{path}/{connectionId}/tools/{toolId}/review").Single(), JsonSerializerOptions.Web)!;
        sent.DefinitionHash.Should().Be(Tool.DefinitionHash);
        model.Tools[connectionId].Single().NeedsReview.Should().BeFalse();
        model.Notice.Should().Be("The change to Sample tool is accepted.");
    }

    [Fact]
    public async Task A_review_conflict_refreshes_the_definition_without_accepting_the_new_one()
    {
        var model = Model();
        await model.Open();
        server.Refuse("POST", $"{path}/{connectionId}/tools/{toolId}/review", HttpStatusCode.Conflict, "Sample tool changed again.");
        server.Answer("GET", $"{path}/{connectionId}/tools", new[] { Tool with { DefinitionHash = "sample-new-definition" } });

        await model.Accept(Tool);

        model.Tools[connectionId].Single().DefinitionHash.Should().Be("sample-new-definition");
        model.Tools[connectionId].Single().NeedsReview.Should().BeTrue();
        model.Problems[connectionId].Should().Be("Sample tool changed again.");
        model.ChangePending.Should().BeFalse();
    }

    [Fact]
    public async Task A_provider_redirect_is_opened_outside_the_app_and_carries_the_requested_return_scheme()
    {
        server.Answer("POST", $"{path}/{connectionId}/sign-in", new Drover.Contracts.Tools.ToolSignInStart("redirect", "https://provider.example.test/authorize", null));
        var model = Model();
        await model.Open();
        string? opened = null;

        await model.SignIn(Connection, address => { opened = address; return Task.CompletedTask; }, "drover");

        opened.Should().Be("https://provider.example.test/authorize");
        server.Sent("POST", $"{path}/{connectionId}/sign-in").Single().Should().Contain("drover");
        navigator.Current.Should().Be(Addresses.Home);
        model.ChangePending.Should().BeFalse();
    }

    [Fact]
    public async Task A_refused_external_launch_reports_on_its_connection_and_unlocks_the_screen()
    {
        server.Answer("POST", $"{path}/{connectionId}/sign-in", new Drover.Contracts.Tools.ToolSignInStart("redirect", "https://provider.example.test/authorize", null));
        var model = Model();
        await model.Open();

        await model.SignIn(Connection, _ => throw new DeviceCallFailedException("Sample launch refused."));

        model.Problems[connectionId].Should().Be("The sign-in page could not be opened. Try again.");
        model.Connections.Should().ContainSingle();
        model.ChangePending.Should().BeFalse();
    }

    [Fact]
    public async Task An_app_return_ticket_is_finished_and_removed_from_navigation()
    {
        var ticket = "sample-one-time-ticket";
        server.Answer("POST", "/api/tool-sign-ins/finish", new Drover.Contracts.Tools.ToolSignInFinish(true, Samples.Session().Spaces[0].Id, connectionId));
        var model = Model();

        await model.Open(ticket: ticket);

        navigator.Current.Should().Be(Addresses.ToolSignIn("connected", connectionId.ToString()));
        navigator.Current.Should().NotContain(ticket);
        model.Notice.Should().Be("You are signed in to Sample tools.");
        server.Sent("POST", "/api/tool-sign-ins/finish").Should().ContainSingle();
    }

    [Fact]
    public async Task A_failed_remove_keeps_the_connection_and_its_tools()
    {
        server.Refuse("DELETE", $"{path}/{connectionId}", HttpStatusCode.Forbidden, "Sample removal refused.");
        var model = Model();
        await model.Open();
        model.Removing = Connection;

        await model.Remove();

        model.Connections.Should().ContainSingle();
        model.Tools[connectionId].Should().ContainSingle();
        model.Problems.Should().ContainKey(connectionId);
        model.Removing.Should().BeNull();
        model.ChangePending.Should().BeFalse();
    }

    #endregion

    #region Private Methods

    private ToolConnectionsViewModel Model() => new(
        server.CreateApi(),
        new(server.CreateApi(), new ShareInbox()),
        navigator,
        NullLogger<ToolConnectionsViewModel>.Instance);

    #endregion
}
