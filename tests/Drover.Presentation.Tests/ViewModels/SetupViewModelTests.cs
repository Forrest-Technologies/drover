namespace Drover.Presentation.Tests.ViewModels;

/// <summary>The three steps of first-run setup: the account, the model, the first bot.</summary>
public sealed class SetupViewModelTests : ServerScreenTest
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly string connections = $"/api/spaces/{SpaceId}/model-connections";

    private static readonly string space = $"/api/spaces/{SpaceId}";

    #endregion

    #region Properties

    private static ModelConnectionSummary Connected { get; } =
        new(connectionId, "Sample connection", "venice", null, true, "connected", null, [], null, null);

    private static BotSummary Marshal { get; } = new(botId, "Marshal", "marshal", "Sample instructions", connectionId, "sample-default", "active");

    #endregion

    #region Create Account

    [Fact]
    public async Task The_account_step_asks_for_the_account_and_the_setup_code_the_server_printed()
    {
        Server.Answer("GET", "/api/setup", new SetupStatus(RequiresCode: true));
        var step = Make<CreateAccountViewModel>();

        step.ShowChecking.Should().BeTrue("nothing is known about the server until it answers");
        await step.CheckServer();

        step.ShowForm.Should().BeTrue();
        step.RequiresCode.Should().BeTrue();
        step.ShowChecking.Should().BeFalse();
        CreateAccountViewModel.Heading.Should().Be("Create your account");
        CreateAccountViewModel.Title.Should().Be("Set up Drover");
        CreateAccountViewModel.PasswordHint.Should().Be("At least 12 characters.");
        CreateAccountViewModel.SetupCodeHint.Should().Be("Drover printed this code in its server log when it started. It proves you run this server.");
        step.SubmitLabel.Should().Be("Create account");
    }

    [Fact]
    public async Task The_setup_code_is_not_asked_for_when_the_server_does_not_need_it()
    {
        Server.Answer("GET", "/api/setup", new SetupStatus(RequiresCode: false));
        var step = Make<CreateAccountViewModel>();

        await step.CheckServer();

        step.ShowForm.Should().BeTrue();
        step.RequiresCode.Should().BeFalse();
    }

    [Fact]
    public async Task Creating_the_account_signs_in_and_moves_to_the_model_step()
    {
        Server.Answer("GET", "/api/setup", new SetupStatus(RequiresCode: true));
        Server.Answer("POST", "/api/setup", Person, HttpStatusCode.Created);
        var step = Make<CreateAccountViewModel>();
        await step.CheckServer();

        step.DisplayName = " Sam Sample ";
        step.Email = "sam@example.org";
        step.Password = "a long sample password";
        step.SetupCode = "ABCD-EFGH-JKLM";
        await step.Submit();

        Navigator.Current.Should().Be(Addresses.SetupModel);
        var sent = JsonSerializer.Deserialize<SetupRequest>(Server.Sent("POST", "/api/setup").Single(), JsonSerializerOptions.Web)!;
        sent.Should().Be(new SetupRequest("sam@example.org", "Sam Sample", "a long sample password", "ABCD-EFGH-JKLM"));
        Session.Current.Should().BeEquivalentTo(Person);
    }

    [Fact]
    public async Task A_wrong_setup_code_is_reported_with_the_servers_sentence()
    {
        Server.Answer("GET", "/api/setup", new SetupStatus(RequiresCode: true));
        Server.Refuse("POST", "/api/setup", HttpStatusCode.Forbidden, "The setup code is required. It is printed in the server log.");
        Navigator.NavigateTo(Addresses.Setup);
        var step = Make<CreateAccountViewModel>();
        await step.CheckServer();

        await step.Submit();

        step.Problem.Should().Be("The setup code is required. It is printed in the server log.");
        step.Busy.Should().BeFalse();
        Navigator.Current.Should().Be(Addresses.Setup);
    }

    [Fact]
    public async Task A_server_that_is_already_set_up_hands_over_to_sign_in()
    {
        var step = Make<CreateAccountViewModel>();

        await step.CheckServer();

        Navigator.Current.Should().Be(Addresses.SignIn);
        Navigator.CanGoBack.Should().BeFalse("setup is not a place to go back to");
    }

    [Fact]
    public async Task A_setup_that_is_finished_while_the_form_is_open_says_so_and_points_to_sign_in()
    {
        Server.Answer("GET", "/api/setup", new SetupStatus(RequiresCode: false));
        Server.Answer("POST", "/api/setup", HttpStatusCode.NotFound);
        var step = Make<CreateAccountViewModel>();
        await step.CheckServer();

        await step.Submit();

        step.Problem.Should().Be("This server already has its first account. Sign in with it.");
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_offers_one_thing_to_do()
    {
        Server.Unreachable = true;
        var step = Make<CreateAccountViewModel>();

        await step.CheckServer();

        step.Problem.Should().Be(Failures.Unreachable);
        step.ShowFailed.Should().BeTrue();
        step.ShowForm.Should().BeFalse();
        step.ShowChecking.Should().BeFalse();

        Server.Unreachable = false;
        Server.Answer("GET", "/api/setup", new SetupStatus(RequiresCode: false));
        await step.CheckServerCommand.ExecuteAsync(null);

        step.ShowForm.Should().BeTrue("trying again asks the server again");
        step.Problem.Should().BeNull();
    }

    #endregion

    #region Connect Model

    [Fact]
    public void The_model_step_offers_two_providers_and_starts_with_the_key_alone()
    {
        SignedIn();
        var step = Make<ConnectModelViewModel>();

        ConnectModelViewModel.Title.Should().Be("Connect a model");
        ConnectModelViewModel.Lead.Should().Be("Bots think with a model you bring. The key is stored encrypted on this server and is never shown again.");
        ConnectModelViewModel.Providers.Select(provider => provider.Label).Should().Equal("Venice", "Compatible endpoint");
        step.Kind.Should().Be("venice");
        step.ShowVenice.Should().BeTrue();
        step.ShowCompatible.Should().BeFalse("the key is all a provider of the catalog needs");
        step.TestLabel.Should().Be("Test connection");
        step.Passed.Should().BeFalse();
        step.NotPassed.Should().BeTrue();
    }

    [Fact]
    public void Choosing_a_compatible_endpoint_asks_for_its_base_address_and_forgets_the_last_answer()
    {
        SignedIn();
        var step = Make<ConnectModelViewModel>();
        step.Problem = "Sample: the last attempt failed.";

        step.Kind = "openai_compatible";

        step.ShowCompatible.Should().BeTrue();
        step.ShowVenice.Should().BeFalse();
        step.Problem.Should().BeNull();
        step.Result.Should().BeNull();
    }

    [Fact]
    public async Task A_passed_test_says_so_in_the_providers_words_and_offers_to_continue()
    {
        SignedIn();
        await Session.Load(TestContext.Current.CancellationToken);
        Server.Answer("POST", connections, Connection("venice", null), HttpStatusCode.Created);
        Server.Answer("POST", $"{connections}/{connectionId}/test", new ConnectionTestResponse(true, "Connected. 42 models are available.", null, Connection("venice", null)));
        var step = Make<ConnectModelViewModel>();

        step.ApiKey = "sample-key";
        await step.Test();

        step.ShownOutcome.Should().Be("Connected. 42 models are available.");
        step.OutcomeKind.Should().Be(NoteKind.Ok);
        step.Passed.Should().BeTrue();
        step.NotPassed.Should().BeFalse("a passed test offers Test again and Continue");
        step.Continue();
        Navigator.Current.Should().Be(Addresses.SetupBot);
    }

    [Fact]
    public async Task A_failed_test_says_why_and_does_not_offer_to_continue()
    {
        SignedIn();
        await Session.Load(TestContext.Current.CancellationToken);
        Server.Answer("POST", connections, Connection("venice", null), HttpStatusCode.Created);
        Server.Answer("POST", $"{connections}/{connectionId}/test", new ConnectionTestResponse(false, "The provider rejected the API key.", "unauthorized", Connection("venice", null)));
        var step = Make<ConnectModelViewModel>();

        step.ApiKey = "wrong-key";
        await step.Test();

        step.ShownOutcome.Should().Be("The provider rejected the API key.");
        step.OutcomeKind.Should().Be(NoteKind.Danger);
        step.Passed.Should().BeFalse();
        step.NotPassed.Should().BeTrue();
    }

    [Fact]
    public async Task While_the_provider_is_asked_the_step_says_so_and_the_note_is_empty()
    {
        SignedIn();
        await Session.Load(TestContext.Current.CancellationToken);
        Server.Answer("POST", connections, Connection("venice", null), HttpStatusCode.Created);
        var holding = Server.Hold("POST", $"{connections}/{connectionId}/test");
        Server.Answer("POST", $"{connections}/{connectionId}/test", new ConnectionTestResponse(true, "Sample: connected.", null, Connection("venice", null)));
        var step = Make<ConnectModelViewModel>();

        var testing = step.Test();
        await WaitUntil(() => step.Busy);

        ConnectModelViewModel.AskingNote.Should().Be("Asking the provider. This can take a few seconds.");
        step.TestLabel.Should().Be("Testing");
        step.ShownOutcome.Should().BeNull();
        holding.SetResult();
        await testing;
        step.TestLabel.Should().Be("Test connection");
    }

    [Fact]
    public async Task Testing_again_with_a_corrected_key_replaces_the_key_and_makes_no_second_connection()
    {
        SignedIn();
        await Session.Load(TestContext.Current.CancellationToken);
        Server.Answer("POST", connections, Connection("venice", null), HttpStatusCode.Created);
        Server.Answer("PUT", $"{connections}/{connectionId}/key", Connection("venice", null));
        Server.Answer("POST", $"{connections}/{connectionId}/test", new ConnectionTestResponse(false, "The provider rejected the API key.", "unauthorized", Connection("venice", null)));
        var step = Make<ConnectModelViewModel>();
        step.ApiKey = "wrong-key";
        await step.Test();

        step.ApiKey = "right-key";
        await step.Test();

        Server.Sent("POST", $"{connections}/{connectionId}/test").Should().HaveCount(2);
        Server.Sent("POST", connections).Should().ContainSingle();
        Server.Sent("PUT", $"{connections}/{connectionId}/key").Single().Should().Contain("right-key");
    }

    [Fact]
    public async Task A_refused_connection_shows_the_servers_sentence()
    {
        SignedIn();
        await Session.Load(TestContext.Current.CancellationToken);
        Server.Refuse("POST", connections, HttpStatusCode.BadRequest, "Enter the address of the server, starting with http:// or https://.");
        var step = Make<ConnectModelViewModel>();
        step.Kind = "openai_compatible";

        await step.Test();

        step.ShownOutcome.Should().Be("Enter the address of the server, starting with http:// or https://.");
        step.OutcomeKind.Should().Be(NoteKind.Danger);
        step.Busy.Should().BeFalse();
    }

    [Fact]
    public async Task Nobody_signed_in_is_sent_to_sign_in_and_is_shown_nothing_of_the_step()
    {
        Server.Answer("GET", "/api/session", HttpStatusCode.Unauthorized);
        var gate = Make<SessionGateViewModel>();

        await gate.Check();

        Navigator.Current.Should().Be(Addresses.SignIn);
        gate.State.Should().NotBe(GateState.SignedIn);
    }

    #endregion

    #region Meet Bot

    [Fact]
    public async Task The_bot_step_asks_one_thing_the_model_and_starts_on_the_providers_default()
    {
        AnswerBotStep();
        ProviderLists(Model("sample-large", []), Model("sample-default", ["default"]));
        var step = Make<MeetBotViewModel>();

        step.ShowLoading.Should().BeTrue();
        await step.Start();

        step.ShowForm.Should().BeTrue();
        step.ChosenModel.Should().Be("sample-default");
        step.Models.Select(entry => entry.DisplayName).Should().Equal("sample-default", "sample-large");
        step.ShowModelList.Should().BeTrue();
        step.ShowModelName.Should().BeFalse();
        step.CreateLabel.Should().Be("Create Marshal");
        MeetBotViewModel.Title.Should().Be("Meet your first bot");
        MeetBotViewModel.FormLead.Should().StartWith("Your first bot leads the crew.");
    }

    [Fact]
    public async Task An_endpoint_that_lists_no_models_asks_for_the_name_instead()
    {
        AnswerBotStep();
        ProviderLists();
        var step = Make<MeetBotViewModel>();

        await step.Start();

        step.ShowModelName.Should().BeTrue();
        step.ShowModelList.Should().BeFalse();
        step.ModelNameHint.Should().Be("This endpoint does not list its models. Type the name it expects.");
    }

    [Fact]
    public async Task Creating_the_bot_shows_it_with_its_name_its_model_and_a_way_into_a_conversation()
    {
        AnswerBotStep();
        ProviderLists(Model("sample-default", ["default"]));
        Server.Answer("POST", $"{space}/bots", Marshal, HttpStatusCode.Created);
        var step = Make<MeetBotViewModel>();
        await step.Start();

        await step.Create();

        step.ShowMet.Should().BeTrue();
        step.ShowForm.Should().BeFalse();
        step.BotName.Should().Be("Marshal");
        step.BotModel.Should().Be("sample-default");
        step.BotHue.Should().Be(BotIdentity.Hue(botId));
        step.TalkLabel.Should().Be("Talk to Marshal");
        step.TalkAddress.Should().Be($"/talk/{Marshal.Id}");
        step.MetLead.Should().Be("Marshal is ready. It leads your crew: the bots it makes for you, and the ones you add, work beneath it.");
        var sent = JsonSerializer.Deserialize<CreateBotRequest>(Server.Sent("POST", $"{space}/bots").Single(), JsonSerializerOptions.Web)!;
        sent.Name.Should().Be("Marshal");
        sent.ModelConnectionId.Should().Be(connectionId);
        sent.Model.Should().Be("sample-default");
        sent.Instructions.Should().StartWith("You are Marshal, the lead of this person's crew of bots").And.Contain("you have crew tools, make it beneath you");
        sent.ParentBotId.Should().BeNull("the first bot leads the crew");
    }

    [Fact]
    public async Task The_lead_takes_the_name_the_person_types_and_is_told_who_it_is_by_that_name()
    {
        AnswerBotStep();
        ProviderLists(Model("sample-default", ["default"]));
        Server.Answer("POST", $"{space}/bots", Marshal with { Name = "Sample lead" }, HttpStatusCode.Created);
        var step = Make<MeetBotViewModel>();
        await step.Start();
        step.Name.Should().Be("Marshal", "the name on offer says the job");

        step.Name = " Sample lead ";
        step.CreateLabel.Should().Be("Create Sample lead");
        await step.Create();

        step.BotName.Should().Be("Sample lead");
        var sent = JsonSerializer.Deserialize<CreateBotRequest>(Server.Sent("POST", $"{space}/bots").Single(), JsonSerializerOptions.Web)!;
        sent.Name.Should().Be("Sample lead");
        sent.Instructions.Should().StartWith("You are Sample lead, the lead of this person's crew of bots");
    }

    [Fact]
    public async Task A_name_left_empty_is_called_the_bot_on_the_button()
    {
        AnswerBotStep();
        ProviderLists(Model("sample-default", ["default"]));
        var step = Make<MeetBotViewModel>();
        await step.Start();

        step.Name = "  ";

        step.CreateLabel.Should().Be("Create the bot");
    }

    [Fact]
    public async Task A_refused_bot_says_why_and_keeps_the_form()
    {
        AnswerBotStep();
        ProviderLists(Model("sample-default", ["default"]));
        Server.Refuse("POST", $"{space}/bots", HttpStatusCode.BadRequest, "Choose a model for the bot.");
        var step = Make<MeetBotViewModel>();
        await step.Start();

        await step.Create();

        step.Problem.Should().Be("Choose a model for the bot.");
        step.Busy.Should().BeFalse("the button can be pressed again");
        step.ShowForm.Should().BeTrue();
    }

    [Fact]
    public async Task A_space_that_already_has_a_bot_meets_that_bot_and_makes_no_other()
    {
        AnswerBotStep();
        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        var step = Make<MeetBotViewModel>();

        await step.Start();

        step.ShowMet.Should().BeTrue();
        step.BotName.Should().Be("Marshal");
        step.ShowForm.Should().BeFalse();
    }

    [Fact]
    public async Task With_no_model_connected_the_step_says_so_and_links_to_the_step_before()
    {
        AnswerBotStep();
        Server.Answer("GET", $"{space}/model-connections", Array.Empty<ModelConnectionSummary>());
        var step = Make<MeetBotViewModel>();

        await step.Start();

        step.ShowNoModel.Should().BeTrue();
        MeetBotViewModel.NoModelTitle.Should().Be("No model is connected");
        MeetBotViewModel.NoModelCause.Should().Be("A bot needs a model to think with, and this space has none yet.");
        MeetBotViewModel.NoModelAction.Should().Be("Connect a model");
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_offers_to_try_again()
    {
        AnswerBotStep();
        Server.Answer("GET", $"{space}/bots", HttpStatusCode.InternalServerError);
        var step = Make<MeetBotViewModel>();

        await step.Start();

        step.LoadProblem.Should().StartWith("The server failed to answer.");
        step.ShowFailed.Should().BeTrue();
        step.ShowLoading.Should().BeFalse();

        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        await step.LoadCommand.ExecuteAsync(null);
        step.ShowMet.Should().BeTrue("trying again asks again");
    }

    [Fact]
    public async Task The_connection_that_last_answered_a_test_is_the_one_the_bot_is_made_with()
    {
        AnswerBotStep();
        var older = Connected with { Id = Guid.Parse("0196f1a0-0000-7000-8000-000000000009"), Status = "untested" };
        Server.Answer("GET", $"{space}/model-connections", new[] { older, Connected });
        ProviderLists(Model("sample-default", ["default"]));
        Server.Answer("POST", $"{space}/bots", Marshal, HttpStatusCode.Created);
        var step = Make<MeetBotViewModel>();
        await step.Start();

        await step.Create();

        JsonSerializer.Deserialize<CreateBotRequest>(Server.Sent("POST", $"{space}/bots").Single(), JsonSerializerOptions.Web)!
            .ModelConnectionId.Should().Be(connectionId);
    }

    #endregion

    #region Private Methods

    private static ModelConnectionSummary Connection(string kind, string? baseUrl) =>
        new(connectionId, "Sample connection", kind, baseUrl, true, "untested", null, [], null, null);

    private static ModelCatalogResponse Model(string id, string[] traits) =>
        new(id, id, new(null, null, true, true, false, false, [], false, false), new(null, null, null, "usd"), null, traits, "available", DateTimeOffset.UnixEpoch);

    private void AnswerBotStep()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());
        Server.Answer("GET", $"{space}/model-connections", new[] { Connected });
    }

    private void ProviderLists(params ModelCatalogResponse[] models) =>
        Server.Answer("POST", $"{space}/model-connections/{connectionId}/models/refresh", new ModelRefreshResponse(true, null, null, models));

    #endregion
}
