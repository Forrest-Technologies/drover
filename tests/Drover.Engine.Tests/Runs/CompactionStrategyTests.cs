namespace Drover.Engine.Tests.Runs;

public sealed class CompactionStrategyTests(PostgresServer postgres)
{
    #region Public Methods

    [Theory]
    [InlineData(CompactionStrategy.Truncation, 1, false)]
    [InlineData(CompactionStrategy.SlidingWindow, 1, false)]
    [InlineData(CompactionStrategy.None, 1, true)]
    [InlineData(CompactionStrategy.Truncation, 99999, true)]
    public async Task The_model_receives_the_selected_history_and_the_transcript_stays_complete(CompactionStrategy strategy, int threshold, bool keepsOld)
    {
        var script = new ScriptedModel([new TextOutput("First answer.")], [new TextOutput("Second answer.")], [new TextOutput("Third answer.")]);
        await using var world = await RunWorld.Create(postgres, script);
        await world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.MemorySettings = new MemorySettings { Compaction = strategy, CompactAtTokens = threshold, KeepRecentMessages = 1 };
            return true;
        });
        world.StartHost();
        var thread = await world.NewThread();
        foreach (var question in new[] { "First task", "Second task", "Third task" })
        {
            var run = await world.WaitForStatus(await world.Post(thread, question), RunStatus.Completed, RunStatus.Failed);
            run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        }

        script.Requests[^1].Any(message => message.Summary == "First task").Should().Be(keepsOld);
        script.Requests[^1].Should().Contain(message => message.Summary == "Third task");
        (await world.ReadMessages(thread)).Should().HaveCount(6);
    }

    [Fact]
    public async Task Tool_results_compaction_keeps_the_conversation_and_shortens_old_tool_groups()
    {
        var tools = new TestTools().Add("test.lookup", ToolSensitivity.Read);
        var script = new ScriptedModel([new ToolCallOutput("first", "test_lookup")], [new TextOutput("Looked it up.")], [new TextOutput("Finished.")]);
        await using var world = await RunWorld.Create(postgres, script, tools);
        await world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.MemorySettings = new MemorySettings { Compaction = CompactionStrategy.ToolResults, CompactAtTokens = 1, KeepRecentMessages = 1 };
            return true;
        });
        world.StartHost();
        var thread = await world.NewThread();
        foreach (var question in new[] { "Look it up", "Continue" })
        {
            var run = await world.WaitForStatus(await world.Post(thread, question), RunStatus.Completed, RunStatus.Failed);
            run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        }

        script.Requests[^1].Should().Contain(message => message.Summary.Contains("[Drover omitted an earlier tool result.]"));
        script.Requests[^1].Should().Contain(message => message.Summary == "Look it up");
        (await world.ReadMessages(thread)).SelectMany(message => message.Parts).Should().ContainSingle(part => part is ToolResultPart);
    }

    #endregion
}
