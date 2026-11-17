namespace Drover.Engine.Tests.Runs;

/// <summary>
/// Output a built-in tool streams while it runs: it reaches the log as <c>tool.output</c> between the call's start and
/// its end, coalesced, redacted, and bounded per call.
/// </summary>
public sealed class RunToolOutputTests(PostgresServer postgres)
{
    #region Public Methods

    [Fact]
    public async Task Output_is_logged_between_the_start_and_the_end_of_its_call_coalesced_and_in_order()
    {
        var tools = new TestTools().AddWithCall("test.print", ToolSensitivity.Read, async (call, _) =>
        {
            for (var line = 0; line < 200; line++)
            {
                await call.Output(new ToolOutputChunk(ToolOutputChunk.Stdout, $"line {line}\n"));
            }

            await call.Output(new ToolOutputChunk(ToolOutputChunk.Stderr, "done\n"));
            return JsonElement.Parse("""{"ok":true}""");
        });
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new ToolCallOutput("c1", "test_print")], [new TextOutput("Printed.")]), tools);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Print");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var events = await world.ReadEvents(runId);
        var types = events.Select(stored => stored.Type).ToList();
        var outputs = events.Where(stored => stored.Type == RunEventTypes.ToolOutput).Select(RunWorld.Data<ToolOutput>).ToList();
        var callId = RunWorld.Data<ToolStarted>(events.Single(stored => stored.Type == RunEventTypes.ToolStarted)).ToolCallId;

        outputs.Should().NotBeEmpty().And.OnlyContain(output => output.ToolCallId == callId);
        outputs.Count.Should().BeLessThan(50, "two hundred small pieces are written as a few larger events");
        string.Concat(outputs.Where(output => output.Stream == ToolOutputChunk.Stdout).Select(output => output.Text))
            .Should().Be(string.Concat(Enumerable.Range(0, 200).Select(line => $"line {line}\n")));
        outputs[^1].Should().Be(new ToolOutput(callId, ToolOutputChunk.Stderr, "done\n"));
        types.IndexOf(RunEventTypes.ToolOutput).Should().BeGreaterThan(types.IndexOf(RunEventTypes.ToolStarted));
        types.LastIndexOf(RunEventTypes.ToolOutput).Should().BeLessThan(types.IndexOf(RunEventTypes.ToolCompleted));
    }

    [Fact]
    public async Task Output_is_redacted_before_it_is_logged()
    {
        var key = RunWorld.NewModelKey();
        var tools = new TestTools().AddWithCall("test.leak", ToolSensitivity.Read, async (call, _) =>
        {
            await call.Output(new ToolOutputChunk(ToolOutputChunk.Stdout, $"the key is {key}\n"));
            return JsonElement.Parse("""{"ok":true}""");
        });
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new ToolCallOutput("c1", "test_leak")], [new TextOutput("Done.")]), tools, key);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Leak");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var events = await world.ReadEvents(runId);
        events.Should().OnlyContain(stored => !stored.Data.Contains(key));
        events.Where(stored => stored.Type == RunEventTypes.ToolOutput).Select(RunWorld.Data<ToolOutput>)
            .Should().ContainSingle().Which.Text.Should().Be($"the key is {Redactor.Mark}\n");
    }

    [Fact]
    public async Task A_call_streams_no_more_than_the_limit_and_the_log_says_once_that_the_rest_is_left_out()
    {
        var tools = new TestTools().AddWithCall("test.flood", ToolSensitivity.Read, async (call, _) =>
        {
            for (var piece = 0; piece < 10; piece++)
            {
                await call.Output(new ToolOutputChunk(ToolOutputChunk.Stdout, new string('x', 40)));
            }

            return JsonElement.Parse("""{"ok":true}""");
        });
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new ToolCallOutput("c1", "test_flood")], [new TextOutput("Done.")]), tools);
        world.StartHost(options => options.MaxToolOutputCharacters = 100);

        var runId = await world.Post(await world.NewThread(), "Flood");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var text = string.Concat((await world.ReadEvents(runId))
            .Where(stored => stored.Type == RunEventTypes.ToolOutput)
            .Select(stored => RunWorld.Data<ToolOutput>(stored).Text));
        text.Should().StartWith(new string('x', 100)).And.NotContain(new string('x', 101));
        text.Should().Contain("at most 100 characters");
        text.Split("not shown here").Should().HaveCount(2, "the note is written once");
    }

    [Fact]
    public async Task A_tool_that_cannot_do_what_was_asked_gives_the_model_its_own_words_as_an_error()
    {
        var tools = new TestTools().AddWithCall("test.refuse", ToolSensitivity.Read, (_, _) =>
            throw new BuiltinToolException("That file is not there."));
        var script = new ScriptedModel([new ToolCallOutput("c1", "test_refuse")], [new TextOutput("Sorry.")]);
        await using var world = await RunWorld.Create(postgres, script, tools);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Read it");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var call = (await world.ReadToolCalls(runId)).Single();
        call.IsError.Should().BeTrue();
        call.Result!.Value.GetProperty("error").GetString().Should().Be("That file is not there.");
        script.Requests[1].Should().Contain(new SeenMessage("tool", """c1 = {"error":"That file is not there."}"""));
    }

    #endregion
}
