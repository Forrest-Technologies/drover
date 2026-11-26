namespace Drover.Engine.Tests.Runs;

public sealed class ContextCheckTests(PostgresServer postgres)
{
    #region Public Methods

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unknown_context_gets_one_shorter_retry_after_the_provider_refuses_it(bool refusesAgain)
    {
        var longAnswer = new string('x', 8000);
        var script = new ScriptedModel([new TextOutput(longAnswer)], [new TextOutput("Finished.")]);
        await using var world = await RunWorld.Create(postgres, script);
        await world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.MemorySettings = new MemorySettings { KeepRecentMessages = 1 };
            return true;
        });
        world.StartHost();
        var thread = await world.NewThread();
        await world.WaitForStatus(await world.Post(thread, "First"), RunStatus.Completed);
        world.Model.BeforeCall = call =>
        {
            if (call == 1 || refusesAgain && call == 2)
            {
                world.Model.FailNext = new ModelProviderException(ModelFailure.TooLong, "Too long.");
            }

            return Task.CompletedTask;
        };

        var run = await world.WaitForStatus(await world.Post(thread, "Continue"), RunStatus.Completed, RunStatus.Failed);
        world.Model.Requests.Should().HaveCount(3);
        TokenEstimate.Request(world.Model.Requests[2]).Should().BeLessThan(TokenEstimate.Request(world.Model.Requests[1]));
        run.Status.Should().Be(refusesAgain ? RunStatus.Failed : RunStatus.Completed);
        if (refusesAgain)
        {
            run.Error!.Code.Should().Be("context_too_long");
        }

        (await world.ReadMessages(thread)).SelectMany(message => message.Parts).OfType<TextPart>().Should().Contain(part => part.Text == longAnswer);
    }

    [Fact]
    public async Task A_single_oversized_message_is_refused_before_the_provider_is_called()
    {
        var script = new ScriptedModel([new TextOutput("Unexpected.")]);
        await using var world = await RunWorld.Create(postgres, script);
        await world.Read(async db =>
        {
            var entry = await db.ModelCatalog.SingleAsync(RunWorld.Token);
            entry.Capabilities = new ModelCapabilities { ContextTokens = 2048 };
            return true;
        });
        world.StartHost();
        var run = await world.WaitForStatus(await world.Post(await world.NewThread(), new string('x', 10000)), RunStatus.Completed, RunStatus.Failed);
        run.Error!.Code.Should().Be("context_too_long");
        world.Model.Requests.Should().BeEmpty();
    }

    #endregion
}
