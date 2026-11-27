namespace Drover.Server.Tests;

/// <summary>
/// A live call with a stand-in model that only holds the conversation: what it hands over becomes the person's
/// message, the bot answers it in a real run, and the run's reply is told to the model to say.
/// </summary>
public sealed class VoiceCallWorkTests(PostgresServer postgres) : VoiceTestBase(postgres)
{
    #region Private Fields

    private ScriptedModel script = new();

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Runs:HeartbeatSeconds"] = "0.2",
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(script));
    }

    #endregion

    #region Tests

    [Fact]
    public async Task Handed_over_work_is_the_persons_message_and_the_bots_run_and_its_reply_is_told_to_the_voice()
    {
        script = new ScriptedModel([new TextOutput("Two things are **waiting**.")]);
        var (client, alice, bot, thread) = await SeedCall();

        using var socket = await Call(client, alice.SpaceId, thread.Id);
        await NextFrame(socket);
        var call = Speech.Calls.Single();
        call.Does(
            new RealtimePersonSaid("Hello."),
            new RealtimeBotSaid("Hi there."),
            new RealtimePersonSaid("What is waiting for me?"),
            new RealtimeWorkHandedOver("work_1"),
            new RealtimeBotSaid("One moment."));
        var report = await call.Reports.ReadAsync(Token).AsTask().WaitAsync(Patience, Token);
        call.Does(
            new RealtimeBotSaid("You have two things waiting."),
            new RealtimePersonSaid("Thanks."),
            new RealtimeBotSaid("Any time."));
        await Send(socket, new VoiceCallFrame(VoiceCallMessages.Hangup));
        var frames = await FramesUntilEnded(socket);
        var messages = await Read<List<MessageResponse>>(await client.Get($"{Space(alice.SpaceId)}/threads/{thread.Id}/messages"));

        call.Options.Instructions.Should().Be($"You are the voice of Helper, an assistant. {VoiceService.SpokenReplyInstruction}");
        call.Options.Instructions.Should().NotContain("Answer briefly.", "the bot's instructions stay with its runs");
        report.Should().Be(("work_1", "Two things are waiting."), "the reply is made plain for speech");
        frames[^1].Reason.Should().Be(VoiceCallEndReasons.HungUp);
        messages.Select(message => (message.Role, message.Parts.Single(part => part.Kind == "text").Text, InARun: message.RunId is not null)).Should().Equal(
            ("user", "Hello.", false),
            ("assistant", "Hi there.", false),
            ("user", "What is waiting for me?", false),
            ("assistant", "Two things are **waiting**.", true),
            ("user", "Thanks.", false),
            ("assistant", "Any time.", false));
        model!.TransactionWasOpen.Should().NotContain(true, "the run a call starts is an ordinary run");
    }

    [Fact]
    public async Task Work_handed_over_with_nothing_asked_starts_no_run_and_says_so()
    {
        var (client, alice, bot, thread) = await SeedCall();

        using var socket = await Call(client, alice.SpaceId, thread.Id);
        await NextFrame(socket);
        var call = Speech.Calls.Single();
        call.Does(new RealtimeWorkHandedOver("work_1"));
        var report = await call.Reports.ReadAsync(Token).AsTask().WaitAsync(Patience, Token);
        await Send(socket, new VoiceCallFrame(VoiceCallMessages.Hangup));
        await FramesUntilEnded(socket);
        var messages = await Read<List<MessageResponse>>(await client.Get($"{Space(alice.SpaceId)}/threads/{thread.Id}/messages"));
        var summary = await Read<ThreadSummary>(await client.Get($"{Space(alice.SpaceId)}/threads/{thread.Id}"));

        report.Should().Be(("work_1", VoiceService.NothingAsked));
        messages.Should().BeEmpty();
        summary.ActiveRunId.Should().BeNull();
    }

    [Fact]
    public async Task Small_talk_while_the_bot_works_is_said_and_not_kept_and_the_call_goes_on()
    {
        var (client, alice, bot, thread) = await SeedCall();

        using var socket = await Call(client, alice.SpaceId, thread.Id);
        await NextFrame(socket);
        await SeedActiveRun(alice.SpaceId, alice.UserId, thread.Id, bot.Id);
        var call = Speech.Calls.Single();
        call.Does(new RealtimePersonSaid("Still there?"), new RealtimeBotSaid("Yes, still working."), new RealtimePersonSaid("Good."));
        (await NextFrame(socket)).Text.Should().Be("Still there?");
        (await NextFrame(socket)).Text.Should().Be("Yes, still working.");
        (await NextFrame(socket)).Text.Should().Be("Good.");
        await Send(socket, new VoiceCallFrame(VoiceCallMessages.Hangup));
        var frames = await FramesUntilEnded(socket);
        var messages = await Read<List<MessageResponse>>(await client.Get($"{Space(alice.SpaceId)}/threads/{thread.Id}/messages"));

        frames.Should().NotContain(frame => frame.Type == VoiceCallMessages.Failed);
        frames[^1].Reason.Should().Be(VoiceCallEndReasons.HungUp);
        messages.Should().BeEmpty("the run is the conversation's one writer");
    }

    #endregion

    #region Private Methods

    /// <summary>A bot that answers with the scripted model, in a space whose live model hands its work over.</summary>
    private async Task<(ApiClient Client, (string Email, string Password, Guid UserId, Guid SpaceId) Person, BotSummary Bot, ThreadSummary Thread)> SeedCall()
    {
        Speech.HandsOver = true;
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var speech = await CreateConnection(client, alice.SpaceId);
        await SeedModels(alice.SpaceId, alice.UserId, speech.Id, ("live-1", new ModelCapabilities { RealtimeVoice = true }));
        var scripted = await Read<ModelConnectionSummary>(await client.Post(
            $"{Space(alice.SpaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted")));
        var bot = await Read<BotSummary>(await client.Post($"{Space(alice.SpaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", scripted.Id, null)));
        var thread = await Read<ThreadSummary>(await client.Put($"{Space(alice.SpaceId)}/bots/{bot.Id}/main-thread"));
        return (client, alice, bot, thread);
    }

    #endregion
}
