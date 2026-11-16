namespace Drover.Client.Tests;

/// <summary>
/// Work handed between bots, against event logs recorded from the run engine: a lead that handed work to Scout, which
/// looked something up and handed part of it to Checker; and a lead whose helper, Scribe, stopped to ask a person.
/// A stream carries the whole tree, and each child's events land under the call that handed it the work.
/// </summary>
public sealed class DelegationReducerTests
{
    #region Private Fields

    private const string TwoLevels = "delegation-two-levels.json";

    private const string ChildWaits = "delegation-child-waits.json";

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    #endregion

    #region The Tree

    [Fact]
    public void A_childs_events_land_under_the_call_that_handed_it_the_work_and_a_grandchilds_under_the_childs()
    {
        var log = Load(TwoLevels);
        var started = log.Where(stored => stored.Type == RunEventTypes.DelegationStarted).Select(RunEventTypes.Read<DelegationStarted>).ToList();

        var view = RunReducer.Apply(RunView.Start(log[0].RunId), log);

        var handed = view.ToolCalls.Should().ContainSingle().Subject;
        handed.ToolName.Should().Be("delegate");
        var scout = handed.Delegation!;
        scout.Should().Match<DelegationView>(work => work.ChildRunId == started[0].ChildRunId && work.ChildBotId == started[0].ChildBotId
            && work.ChildThreadId == started[0].ChildThreadId && work.ChildThreadId != null && work.Outcome == "completed" && !work.IsOut);
        scout.Answer.Should().Be("Three sources, one line each. The second was checked.");
        scout.Child.Status.Should().Be("completed");
        scout.Child.Messages.SelectMany(message => message.Parts).Where(part => part.Kind == "text").Select(part => part.Text)
            .Should().Equal("Looking them up, and asking Checker about the second.", "Three sources, one line each. The second was checked.");
        scout.Child.ToolCalls.Select(tool => (tool.ToolName, tool.State)).Should().Equal(("test.lookup", "completed"), ("delegate", "completed"));

        var checker = scout.Child.ToolCalls[1].Delegation!;
        checker.ChildRunId.Should().Be(started[1].ChildRunId);
        checker.Answer.Should().Be("The second source holds up.");
        checker.Child.Messages.Should().ContainSingle().Which.Parts.Single().Text.Should().Be("The second source holds up.");

        view.Messages.SelectMany(message => message.Parts).Where(part => part.Kind == "text").Select(part => part.Text)
            .Should().Equal(["Scout found three sources."], "the children's words stay with their work, not in the lead's reply");
        view.Holds(checker.ChildRunId).Should().BeTrue();
    }

    [Theory]
    [InlineData(TwoLevels)]
    [InlineData(ChildWaits)]
    public void Any_prefix_of_a_tree_log_and_then_the_rest_ends_in_the_same_state_as_the_whole_log(string fixture)
    {
        var log = Load(fixture);
        var whole = Snapshot(RunReducer.Apply(RunView.Start(log[0].RunId), log));

        for (var split = 0; split <= log.Count; split++)
        {
            var resumed = RunReducer.Apply(RunReducer.Apply(RunView.Start(log[0].RunId), log.Take(split)), log);

            Snapshot(resumed).Should().Be(whole, $"a reconnect after event {split} replays from the start and must end in the same place");
        }
    }

    [Fact]
    public void A_childs_approval_is_its_own_calls_and_leaves_the_work_out()
    {
        var log = Load(ChildWaits);

        var view = RunReducer.Apply(RunView.Start(log[0].RunId), log);

        view.Status.Should().Be("waiting");
        var work = view.ToolCalls.Single().Delegation!;
        work.IsOut.Should().BeTrue();
        work.Child.Status.Should().Be("waiting");
        work.Child.ToolCalls.Should().ContainSingle().Which.Should().Match<ToolCallView>(tool => tool.State == "awaiting_approval" && tool.RequestId != null);
        view.ToolCalls.Single().State.Should().Be("running", "the call that handed the work over stays open while its child waits");
    }

    #endregion

    #region History

    [Fact]
    public void The_threads_tool_events_rebuild_the_same_work_as_the_live_stream()
    {
        var log = Load(TwoLevels);
        var live = RunReducer.Apply(RunView.Start(log[0].RunId), log).ToolCalls.Single();

        var replayed = ToolHistory.From(Load("delegation-two-levels.tool-events.json")).Should().ContainSingle().Subject.Value;

        Shape(replayed).Should().Be(Shape(live));
        replayed.Delegation!.Child.ToolCalls[1].Delegation!.Answer.Should().Be("The second source holds up.");
    }

    [Fact]
    public void The_active_runs_whole_tree_is_left_out_of_history()
    {
        var events = Load("delegation-two-levels.tool-events.json");

        ToolHistory.From(events, activeRunId: events[0].RunId).Should().BeEmpty("a child's events are the active run's, not a past run's");
    }

    #endregion

    #region Private Methods

    private static List<RunEventResponse> Load(string fixture) =>
        JsonSerializer.Deserialize<List<RunEventResponse>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), json)!;

    private static string Snapshot(RunView view) => JsonSerializer.Serialize(view, json);

    // What a screen shows of a call and the work under it. How far each run's stream had got, and the step a message
    // streamed in, are how it arrived, not what it is.
    private static string Shape(ToolCallView call) => JsonSerializer.Serialize(Normalize(call), json);

    private static ToolCallView Normalize(ToolCallView call) =>
        call with { Delegation = call.Delegation is { } work ? work with { Child = Normalize(work.Child) } : null };

    private static RunView Normalize(RunView run) =>
        run with
        {
            LastSeq = 0,
            Messages = [.. run.Messages.Select(message => message with { Step = null })],
            ToolCalls = [.. run.ToolCalls.Select(Normalize)],
        };

    #endregion
}
