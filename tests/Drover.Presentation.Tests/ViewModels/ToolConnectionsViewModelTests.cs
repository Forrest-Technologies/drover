namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// The tool connections screen against a stand-in server: what each state says, what the form sends, and what a
/// person who is not an admin may do. Every name, address, and value here is a sample.
/// </summary>
public sealed class ToolConnectionsViewModelTests : ServerScreenTest
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid toolId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly string root = $"/api/spaces/{SpaceId}/tool-connections";

    #endregion

    #region List

    [Fact]
    public async Task Each_server_says_in_words_whether_it_answers_how_it_signs_in_and_what_went_wrong()
    {
        var failed = Connection("failed") with { Id = Guid.Parse("0196f1a0-0000-7000-8000-000000000005"), Name = "Quiet server", LastError = "Sample failure." };
        Listed([Connection("ready") with { LastListedAt = DateTimeOffset.UnixEpoch }, failed], Tool());
        Server.Answer("GET", $"{root}/{failed.Id}/tools", Array.Empty<ConnectionToolSummary>());
        var screen = Make<ToolConnectionsViewModel>();

        await screen.Open(null, null, null);

        screen.Cards.Select(card => card.Name).Should().Equal("Sample server", "Quiet server");
        ToolConnectionsViewModel.Title.Should().Be("Tool connections");
        screen.Cards.Select(card => card.StatusWords).Should().Equal("Connected", "Not connected");
        screen.Cards.Select(card => card.Url).Should().AllBe("https://tools.example.test/mcp");
        screen.Cards[0].Facts.Should().StartWith("no credential · 1 tool · listed ");
        screen.Cards[1].ShownError.Should().Be("Sample failure.");
        screen.Cards[0].ShownError.Should().BeNull();
        screen.Cards[0].ToolRows.Single().Title.Should().Be("Look up");
    }

    [Fact]
    public async Task A_space_with_no_server_says_what_one_is_for_and_offers_to_add_it()
    {
        Listed([]);
        var screen = Make<ToolConnectionsViewModel>();

        await screen.Open(null, null, null);

        screen.ShowEmptyToAdd.Should().BeTrue();
        ToolConnectionsViewModel.EmptyTitle.Should().Be("No tool server yet");
        ToolConnectionsViewModel.EmptyCause.Should().Be("A tool server gives bots tools beyond the built-in ones. Add one by its address, then give its tools to a bot.");
        ToolConnectionsViewModel.AddLabel.Should().Be("Add a tool server");
    }

    [Fact]
    public async Task A_member_sees_the_servers_and_may_sign_in_for_themselves_and_change_nothing_else()
    {
        SignedInAs("member");
        var own = Connection("ready") with { AuthKind = "oauth", SignInScope = "per_user", SignIn = "needs_sign_in" };
        var shared = own with { Id = Guid.Parse("0196f1a0-0000-7000-8000-000000000005"), Name = "Shared server", SignInScope = "space" };
        Server.Answer("GET", root, new[] { own, shared });
        Server.Answer("GET", $"{root}/{own.Id}/tools", new[] { Tool() with { NeedsReview = true } });
        Server.Answer("GET", $"{root}/{shared.Id}/tools", Array.Empty<ConnectionToolSummary>());
        var screen = Make<ToolConnectionsViewModel>();

        await screen.Open(null, null, null);

        screen.Cards.Should().HaveCount(2);
        screen.ShowAddButton.Should().BeFalse();
        screen.Cards.Select(card => card.ShowSignInButton).Should().Equal(true, false);
        screen.Cards.Select(card => card.SignInNeed).Should().Equal(
            "You have not signed in to this server yet.",
            "Nobody has signed in to this server for the space yet.");
        screen.Cards[1].ShowSignInHint.Should().BeTrue();
        ToolConnectionCardModel.SignInHint.Should().Be("An owner or admin of the space signs in for everyone.");
        screen.Cards.Should().OnlyContain(card => !card.CanEdit && !card.ShowReplaceHeaderButton);
        screen.Cards[0].ToolRows.Single().ShowAccept.Should().BeFalse("a member cannot accept a change");
    }

    [Fact]
    public async Task A_member_of_a_space_with_no_server_is_told_who_can_add_one()
    {
        SignedInAs("member");
        Server.Answer("GET", root, Array.Empty<ToolConnectionSummary>());
        var screen = Make<ToolConnectionsViewModel>();

        await screen.Open(null, null, null);

        screen.ShowEmptyReadOnly.Should().BeTrue();
        ToolConnectionsViewModel.EmptyReadOnlyNote.Should().Be("This space has no tool servers yet. An owner or admin of the space can add one.");
    }

    [Fact]
    public async Task A_server_that_fails_to_load_says_so_and_offers_to_try_again()
    {
        SignedIn();
        Server.Answer("GET", root, HttpStatusCode.ServiceUnavailable);
        var screen = Make<ToolConnectionsViewModel>();

        await screen.Open(null, null, null);

        screen.ShowFailed.Should().BeTrue();
        ToolConnectionsViewModel.LoadFailedTitle.Should().Be("Drover could not load the tool connections");

        Server.Answer("GET", root, Array.Empty<ToolConnectionSummary>());
        await screen.LoadCommand.ExecuteAsync(null);
        screen.ShowFailed.Should().BeFalse();
        screen.ShowEmptyToAdd.Should().BeTrue();
    }

    [Fact]
    public async Task The_list_says_it_is_loading_until_the_server_answers()
    {
        Listed([]);
        var loading = Server.Hold("GET", root);
        var screen = Make<ToolConnectionsViewModel>();

        var opened = screen.Open(null, null, null);
        await WaitUntil(() => screen.ShowLoading);
        ToolConnectionsViewModel.LoadingNote.Should().Be("Loading the tool connections.");
        loading.SetResult();
        await opened;

        screen.ShowLoading.Should().BeFalse();
    }

    #endregion

    #region Adding

    [Fact]
    public async Task The_form_asks_for_a_credential_only_when_one_is_chosen_and_sends_only_what_that_choice_uses()
    {
        Listed([]);
        Server.Answer("POST", root, new ToolConnectionRefresh(Connection("ready"), [Tool()], null, 1, 0, 0, 0), HttpStatusCode.Created);
        var screen = Make<ToolConnectionsViewModel>();
        await screen.Open(null, null, null);
        screen.StartAddingCommand.Execute(null);
        var form = screen.Form!;

        var plain = (form.ShowHeader, form.ShowSignIn, form.ShowOwnClient);
        form.AuthKind = "header";
        var header = (form.ShowHeader, form.ShowSignIn, form.ShowOwnClient);
        form.HeaderValue = "sample-header-value";
        form.AuthKind = "oauth";
        var signIn = (form.ShowHeader, form.ShowSignIn, form.ShowOwnClient);
        form.OwnClient = true;
        var ownClient = (form.ShowHeader, form.ShowSignIn, form.ShowOwnClient);
        form.Name = "Sample server";
        form.Url = "https://tools.example.test/mcp";
        form.ClientId = "sample-client";
        form.Issuer = "https://id.example.test";
        await form.AddCommand.ExecuteAsync(null);

        plain.Should().Be((false, false, false));
        header.Should().Be((true, false, false));
        signIn.Should().Be((false, true, false));
        ownClient.Should().Be((false, true, true));
        screen.Cards.Single().Name.Should().Be("Sample server");
        var sent = Server.Sent("POST", root).Should().ContainSingle().Subject;
        sent.Should().Contain("\"authKind\":\"oauth\"").And.Contain("\"clientId\":\"sample-client\"").And.Contain("\"issuer\":\"https://id.example.test\"")
            .And.Contain("\"signInScope\":\"per_user\"").And.Contain("\"trust\":\"untrusted\"");
        sent.Should().NotContain("sample-header-value", "a value typed under a choice that was abandoned is not sent");
        screen.Form.Should().BeNull("the form closes, and what was typed into it is gone");
        screen.Notice.Should().Contain("Sample server is added.");
        screen.NoticeKind.Should().Be(NoteKind.Ok);
    }

    [Fact]
    public async Task The_form_names_what_each_choice_means()
    {
        Listed([]);
        var screen = Make<ToolConnectionsViewModel>();
        await screen.Open(null, null, null);
        screen.StartAddingCommand.Execute(null);
        var form = screen.Form!;

        AddConnectionFormViewModel.Kinds.Select(kind => kind.Label).Should().Equal("Nothing", "A header", "A sign-in");
        AddConnectionFormViewModel.SignInScopes.Select(scope => scope.Label).Should().Equal(
            "Each person signs in as themselves", "One sign-in for the whole space");
        AddConnectionFormViewModel.ProtocolModes.Select(mode => mode.Label).Should().Equal(
            "Chosen by Drover", "Without sessions only", "With sessions only");
        form.ScopeMeaning.Should().Be("A bot run acts as the person it runs for, and can use this server once that person has signed in.");
        form.SignInScope = "space";
        form.ScopeMeaning.Should().Be("Every bot run in the space acts as the one account an owner or admin signs in with.");
        form.AuthKind = "oauth";
        form.ShowNoOwnClient.Should().BeTrue("without a registered client Drover introduces itself");
        form.SubmitLabel.Should().Be("Add server");
    }

    [Fact]
    public async Task A_refused_server_says_what_the_server_said_and_keeps_the_form()
    {
        Listed([]);
        Server.Refuse("POST", root, HttpStatusCode.BadRequest, "Enter the server's web address, starting with https:// or http://, with no user name or password in it.");
        var screen = Make<ToolConnectionsViewModel>();
        await screen.Open(null, null, null);
        screen.StartAddingCommand.Execute(null);
        var form = screen.Form!;

        form.Name = "Sample server";
        await form.AddCommand.ExecuteAsync(null);

        form.Problem.Should().StartWith("Enter the server's web address");
        form.Name.Should().Be("Sample server");
        form.Busy.Should().BeFalse();
        screen.Form.Should().BeSameAs(form);
        screen.ShowEmptyToAdd.Should().BeFalse("while the form is open it is the only thing to do");
    }

    [Fact]
    public async Task Cancelling_drops_the_form_and_what_was_typed_into_it()
    {
        Listed([]);
        var screen = Make<ToolConnectionsViewModel>();
        await screen.Open(null, null, null);
        screen.StartAddingCommand.Execute(null);
        screen.Form!.Name = "Sample server";

        screen.Form.CancelCommand.Execute(null);
        screen.StartAddingCommand.Execute(null);

        screen.Form!.Name.Should().BeEmpty();
    }

    #endregion

    #region Sign In

    [Fact]
    public async Task Signing_in_sends_the_browser_to_the_providers_page()
    {
        Listed([Connection("ready") with { AuthKind = "oauth", SignInScope = "per_user", SignIn = "needs_sign_in" }], Tool());
        Server.Answer("POST", $"{root}/{connectionId}/sign-in", new ToolSignInStart("redirect", "https://id.example.test/authorize?sample=1", null));
        var screen = Make<ToolConnectionsViewModel>();
        List<string> sent = [];
        screen.ProviderPageRequested += sent.Add;
        await screen.Open(null, null, null);

        await screen.Cards.Single().SignInCommand.ExecuteAsync(null);

        sent.Should().Equal("https://id.example.test/authorize?sample=1");
    }

    [Fact]
    public async Task A_sign_in_that_cannot_start_says_why_on_its_server()
    {
        Listed([Connection("ready") with { AuthKind = "oauth", SignIn = "needs_reconnect" }], Tool());
        Server.Answer("POST", $"{root}/{connectionId}/sign-in", new ToolSignInStart("failed", null, "Sample reason the sign-in could not start."));
        var screen = Make<ToolConnectionsViewModel>();
        await screen.Open(null, null, null);
        var card = screen.Cards.Single();

        card.SignInNeed.Should().Be("The space's sign-in to this server has lapsed. Sign in again to keep using its tools.");
        card.SignInLabel.Should().Be("Sign in again");
        await card.SignInCommand.ExecuteAsync(null);

        card.ShownProblem.Should().Be("Sample reason the sign-in could not start.");
        screen.Notice.Should().BeNull();
    }

    [Fact]
    public async Task A_sign_in_that_finishes_at_once_says_so_and_signing_out_says_so()
    {
        var signedOut = Connection("ready") with { AuthKind = "oauth", SignIn = "needs_sign_in" };
        var signedIn = signedOut with { SignIn = "signed_in" };
        Listed([signedOut], Tool());
        Server.Answer("POST", $"{root}/{connectionId}/sign-in", new ToolSignInStart("connected", null, null));
        Server.Answer("GET", root, new[] { signedIn });
        var screen = Make<ToolConnectionsViewModel>();
        await screen.Open(null, null, null);

        await screen.Cards.Single().SignInCommand.ExecuteAsync(null);

        screen.Notice.Should().Be("You are signed in to Sample server.");
        screen.Cards.Single().ShowSignOut.Should().BeTrue();
        screen.Cards.Single().SignOutLabel.Should().Be("Sign out of Sample server");

        Server.Answer("DELETE", $"{root}/{connectionId}/sign-in", HttpStatusCode.NoContent);
        Server.Answer("GET", root, new[] { signedOut });
        await screen.Cards.Single().SignOutCommand.ExecuteAsync(null);

        screen.Notice.Should().Be("The sign-in to Sample server is removed.");
        screen.Cards.Single().ShowSignOut.Should().BeFalse();
    }

    [Fact]
    public async Task A_check_that_fails_says_what_went_wrong_once()
    {
        Listed([Connection("ready")], Tool());
        Server.Answer("POST", $"{root}/{connectionId}/refresh", new ToolConnectionRefresh(Connection("failed") with { LastError = "Sample failure." }, [Tool()], "Sample failure.", 0, 0, 0, 0));
        var screen = Make<ToolConnectionsViewModel>();
        await screen.Open(null, null, null);
        var card = screen.Cards.Single();
        card.CheckLabel.Should().Be("Check and list tools");

        await card.RefreshCommand.ExecuteAsync(null);

        card.StatusWords.Should().Be("Not connected");
        new[] { card.ShownError, card.ShownProblem }.Where(words => words is not null).Should().Equal("Sample failure.");
    }

    [Fact]
    public async Task A_check_says_what_it_listed()
    {
        Listed([Connection("ready")], Tool());
        Server.Answer("POST", $"{root}/{connectionId}/refresh", new ToolConnectionRefresh(Connection("ready"), [Tool(), Tool() with { Id = Guid.NewGuid() }], null, 1, 1, 1, 0));
        var screen = Make<ToolConnectionsViewModel>();
        await screen.Open(null, null, null);

        await screen.Cards.Single().RefreshCommand.ExecuteAsync(null);

        screen.Notice.Should().Be("Sample server: 2 tools listed, 1 new, 1 changed, 1 no longer listed.");
    }

    [Theory]
    [InlineData("connected", "You are signed in to Sample server.", NoteKind.Ok)]
    [InlineData("failed", "The sign-in to Sample server did not finish. Nothing was stored. Try again.", NoteKind.Danger)]
    public async Task Coming_back_from_the_provider_says_how_it_went(string outcome, string words, NoteKind kind)
    {
        Listed([Connection("ready") with { AuthKind = "oauth", SignIn = "signed_in" }], Tool());
        var screen = Make<ToolConnectionsViewModel>();

        await screen.Open(outcome, connectionId, null);

        screen.Notice.Should().Be(words);
        screen.NoticeKind.Should().Be(kind);
    }

    [Fact]
    public async Task An_outcome_the_server_did_not_choose_says_nothing()
    {
        Listed([Connection("ready")], Tool());
        var screen = Make<ToolConnectionsViewModel>();

        await screen.Open("sample-made-up-word", connectionId, null);

        screen.Notice.Should().BeNull("the address carries only one of two words this server chose");
    }

    [Fact]
    public async Task A_native_app_asks_for_the_browser_to_come_back_into_it()
    {
        Services.AddSingleton(new AppSignInReturn("app.example.test"));
        Listed([Connection("ready") with { AuthKind = "oauth", SignInScope = "per_user", SignIn = "needs_sign_in" }], Tool());
        Server.Answer("POST", $"{root}/{connectionId}/sign-in", new ToolSignInStart("redirect", "https://id.example.test/authorize?sample=1", null));
        var screen = Make<ToolConnectionsViewModel>();
        List<string> sent = [];
        screen.ProviderPageRequested += sent.Add;
        await screen.Open(null, null, null);

        await screen.Cards.Single().SignInCommand.ExecuteAsync(null);

        sent.Should().Equal("https://id.example.test/authorize?sample=1");
        Server.Sent("POST", $"{root}/{connectionId}/sign-in").Should().Equal("""{"returnTo":"app.example.test"}""");
    }

    [Theory]
    [InlineData(true, "You are signed in to Sample server.", NoteKind.Ok, "connected")]
    [InlineData(false, "The sign-in to Sample server did not finish. Nothing was stored. Try again.", NoteKind.Danger, "failed")]
    public async Task A_browser_that_came_back_into_the_app_ends_the_sign_in_once_and_says_how_it_went(bool connected, string words, NoteKind kind, string outcome)
    {
        Listed([Connection("ready") with { AuthKind = "oauth", SignIn = "signed_in" }], Tool());
        Server.Answer("POST", "/api/tool-sign-ins/finish", new ToolSignInFinish(connected, SpaceId, connectionId));
        var screen = Make<ToolConnectionsViewModel>();

        await screen.Open(null, null, "sample-ticket");

        screen.Notice.Should().Be(words);
        screen.NoticeKind.Should().Be(kind);
        Server.Sent("POST", "/api/tool-sign-ins/finish").Should().Equal("""{"ticket":"sample-ticket"}""");
        Navigator.Current.Should().Be($"/tools?sign-in={outcome}&connection={connectionId}", "a reload must not present the ticket again");
        Navigator.CanGoBack.Should().BeFalse("the address with the ticket is replaced, not kept");
    }

    [Fact]
    public async Task A_ticket_the_server_cannot_be_asked_about_reads_as_a_sign_in_that_did_not_finish()
    {
        Listed([Connection("ready") with { AuthKind = "oauth", SignIn = "needs_sign_in" }], Tool());
        Server.Refuse("POST", "/api/tool-sign-ins/finish", HttpStatusCode.ServiceUnavailable, "Sample outage.");
        var screen = Make<ToolConnectionsViewModel>();

        await screen.Open(null, null, "sample-ticket");

        screen.Notice.Should().Be("The sign-in to the tool server did not finish. Nothing was stored. Try again.");
        screen.NoticeKind.Should().Be(NoteKind.Danger);
        Navigator.Current.Should().Be("/tools?sign-in=failed");
    }

    #endregion

    #region Review And Remove

    [Fact]
    public async Task A_changed_tool_shows_what_was_reviewed_beside_what_it_says_now_and_can_be_accepted()
    {
        var changed = Tool() with { Description = "Sample new words", NeedsReview = true, Reviewed = new ToolDefinitionSnapshot("Look up", "Sample old words", "{}") };
        Listed([Connection("ready")], changed);
        Server.Answer("POST", $"{root}/{connectionId}/tools/{toolId}/review", changed with { NeedsReview = false, Reviewed = null });
        var screen = Make<ToolConnectionsViewModel>();
        await screen.Open(null, null, null);
        var card = screen.Cards.Single();
        var row = card.ToolRows.Single();

        card.ToolsOpen.Should().BeTrue("a server with something to review shows its tools");
        card.Facts.Should().Contain("1 to review");
        row.NeedsReview.Should().BeTrue();
        row.ReviewedWords.Should().Be("Look up\nSample old words");
        row.NowWords.Should().Be("Look up\nSample new words");
        row.ShowSameWords.Should().BeFalse();
        row.AcceptLabel.Should().Be("Accept the change to Look up");
        ConnectionToolRow.ReviewBadge.Should().Be("Changed, needs review");
        await row.AcceptCommand.ExecuteAsync(null);

        screen.Cards.Single().ToolRows.Should().OnlyContain(tool => !tool.NeedsReview);
        screen.Notice.Should().Contain("The change to Look up is accepted.");
    }

    [Fact]
    public async Task A_tool_whose_words_did_not_change_says_what_did()
    {
        var changed = Tool() with { NeedsReview = true, Reviewed = new ToolDefinitionSnapshot("Look up", "Sample description", "{}") };
        Listed([Connection("ready")], changed);
        var screen = Make<ToolConnectionsViewModel>();

        await screen.Open(null, null, null);

        screen.Cards.Single().ToolRows.Single().ShowSameWords.Should().BeTrue();
        ConnectionToolRow.SameWordsNote.Should().Be("The words are the same. What the tool accepts as input changed.");
    }

    [Fact]
    public async Task A_tool_the_server_no_longer_lists_says_no_bot_can_use_it()
    {
        Listed([Connection("ready")], Tool() with { State = "missing", Description = string.Empty });
        var screen = Make<ToolConnectionsViewModel>();

        await screen.Open(null, null, null);

        var row = screen.Cards.Single().ToolRows.Single();
        row.Missing.Should().BeTrue();
        row.HasDescription.Should().BeFalse();
        ConnectionToolRow.MissingNote.Should().Be("The server no longer lists this tool, so no bot can use it.");
    }

    [Fact]
    public async Task A_server_that_answers_and_lists_no_tools_says_so()
    {
        Listed([Connection("ready")]);
        var screen = Make<ToolConnectionsViewModel>();

        await screen.Open(null, null, null);

        screen.Cards.Single().ShowNoTools.Should().BeTrue();
        screen.Cards.Single().HasTools.Should().BeFalse();
        ToolConnectionCardModel.NoToolsNote.Should().Be("The server answers and lists no tools.");
    }

    [Fact]
    public async Task Accepting_a_tool_that_changed_again_meanwhile_accepts_nothing_and_shows_what_it_says_now()
    {
        var read = Tool() with { Description = "Sample new words", NeedsReview = true, DefinitionHash = "sample-hash-read", Reviewed = new ToolDefinitionSnapshot("Look up", "Sample old words", "{}") };
        Listed([Connection("ready")], read);
        Server.Refuse("POST", $"{root}/{connectionId}/tools/{toolId}/review", HttpStatusCode.Conflict, "This tool changed again while you were reading it. Read what it says now, then accept it.");
        var screen = Make<ToolConnectionsViewModel>();
        await screen.Open(null, null, null);
        Server.Answer("GET", $"{root}/{connectionId}/tools", new[] { read with { Description = "Sample newest words", DefinitionHash = "sample-hash-newest" } });

        await screen.Cards.Single().ToolRows.Single().AcceptCommand.ExecuteAsync(null);

        screen.Cards.Single().ToolRows.Single().NowWords.Should().Be("Look up\nSample newest words");
        screen.Cards.Single().ToolRows.Single().ReviewedWords.Should().Be("Look up\nSample old words");
        Server.Sent("POST", $"{root}/{connectionId}/tools/{toolId}/review").Should().ContainSingle().Which.Should().Contain("sample-hash-read", "the yes names the definition that was on the screen");
        screen.Cards.Single().ShownProblem.Should().Be("This tool changed again while you were reading it. Read what it says now, then accept it.");
    }

    [Fact]
    public async Task Removing_a_server_asks_first_and_says_what_goes_with_it()
    {
        Listed([Connection("ready")], Tool());
        Server.Answer("DELETE", $"{root}/{connectionId}", HttpStatusCode.NoContent);
        var screen = Make<ToolConnectionsViewModel>();
        await screen.Open(null, null, null);

        screen.Cards.Single().RemoveCommand.Execute(null);
        var asked = Server.Sent("DELETE", $"{root}/{connectionId}").Count();
        screen.RemoveOpen.Should().BeTrue();
        screen.RemoveTitle.Should().Be("Remove Sample server");
        ToolConnectionsViewModel.RemoveBody.Should().Contain("Bots lose its tools at once");
        screen.Cards.Single().RemoveLabel.Should().Be("Remove Sample server");
        await screen.RemoveCommand.ExecuteAsync(null);

        asked.Should().Be(0, "nothing is removed before the person confirms");
        screen.Cards.Should().BeEmpty();
        screen.RemoveOpen.Should().BeFalse();
        Server.Sent("DELETE", $"{root}/{connectionId}").Should().ContainSingle();
        screen.Notice.Should().Be("Sample server is removed.");
    }

    [Fact]
    public async Task Keeping_the_server_closes_the_question_and_removes_nothing()
    {
        Listed([Connection("ready")], Tool());
        var screen = Make<ToolConnectionsViewModel>();
        await screen.Open(null, null, null);
        screen.Cards.Single().RemoveCommand.Execute(null);

        screen.KeepServerCommand.Execute(null);

        screen.RemoveOpen.Should().BeFalse();
        screen.Cards.Should().ContainSingle();
        Server.Sent("DELETE", $"{root}/{connectionId}").Should().BeEmpty();
    }

    [Fact]
    public async Task Escape_in_the_question_closes_it_as_keeping_does()
    {
        Listed([Connection("ready")], Tool());
        var screen = Make<ToolConnectionsViewModel>();
        await screen.Open(null, null, null);
        screen.Cards.Single().RemoveCommand.Execute(null);

        screen.RemoveOpen = false;

        screen.RemoveOpen.Should().BeFalse();
        screen.Cards.Should().ContainSingle();
    }

    [Fact]
    public async Task A_header_is_replaced_without_ever_showing_the_old_one()
    {
        Listed([Connection("ready") with { AuthKind = "header" }], Tool());
        Server.Answer("PUT", $"{root}/{connectionId}/header", Connection("ready") with { AuthKind = "header" });
        var screen = Make<ToolConnectionsViewModel>();
        await screen.Open(null, null, null);
        var card = screen.Cards.Single();

        card.ReplacingHeader.Should().BeFalse("a stored credential has no field until it is being replaced");
        card.ShowReplaceHeaderButton.Should().BeTrue();
        card.StartReplacingHeaderCommand.Execute(null);
        card.ReplacingHeader.Should().BeTrue();
        card.ShowReplaceHeaderButton.Should().BeFalse();
        card.HeaderValue.Should().BeEmpty();
        card.HeaderName.Should().Be("Authorization");
        card.HeaderValue = "sample-new-value";
        await card.SaveHeaderCommand.ExecuteAsync(null);

        card.ReplacingHeader.Should().BeFalse();
        card.HeaderValue.Should().BeEmpty();
        Server.Sent("PUT", $"{root}/{connectionId}/header").Should().ContainSingle().Which.Should().Contain("\"headerValue\":\"sample-new-value\"");
        screen.Notice.Should().Be("The header for Sample server is replaced.").And.NotContain("sample-new-value");
    }

    #endregion

    #region Helpers

    private static ToolConnectionSummary Connection(string status) =>
        new(connectionId, "Sample server", "sample", "https://tools.example.test/mcp", "none", "untrusted", "automatic", status, null, null, null);

    private static ConnectionToolSummary Tool() =>
        new(toolId, connectionId, "lookup", "sample_lookup", "Look up", "Sample description", "write", "available", DateTimeOffset.UnixEpoch);

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
