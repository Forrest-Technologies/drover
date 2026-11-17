namespace Drover.Client.Tests;

/// <summary>
/// The tree reducer against a log recorded from the run engine on a real server: Marshal handed work to Scout, Scout
/// handed part of it to Checker, Checker's clock check waited for a person, and so did Marshal's. The root run's one
/// stream carries all three runs.
/// </summary>
public sealed class RunTreeReducerTests
{
    #region Private Fields

    private const string ThreeBots = "three-bots-two-levels.json";

    private static readonly Guid marshal = Guid.Parse("01a1e4c5-76f6-733b-937a-479c62f1ce50");

    private static readonly Guid scout = Guid.Parse("01a1e4c5-788a-7364-b7be-858e25e736da");

    private static readonly Guid checker = Guid.Parse("01a1e4c5-78cb-7812-af8d-160a1fb98db2");

    private static readonly Guid rootRun = Guid.Parse("01a1e4c6-5ac3-7f14-a780-5b12d7ffa2bb");

    private static readonly Guid scoutRun = Guid.Parse("01a1e4c6-5dce-76f3-8879-ec7fad8ce7eb");

    private static readonly Guid checkerRun = Guid.Parse("01a1e4c6-60f8-74f2-ab5e-446e98dd265d");

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    #endregion

    #region The Tree From One Stream

    [Fact]
    public void One_stream_gives_every_run_of_the_tree_with_its_bot_and_every_moment_in_the_order_it_happened()
    {
        var tree = RunTreeReducer.Apply(RunTreeView.Start(rootRun, marshal), Load());

        tree.Runs.Keys.Should().BeEquivalentTo([rootRun, scoutRun, checkerRun]);
        tree.Bots.Should().BeEquivalentTo(new Dictionary<Guid, Guid> { [rootRun] = marshal, [scoutRun] = scout, [checkerRun] = checker });
        tree.Runs.Values.Should().OnlyContain(run => run.Status == "completed");
        tree.LostEarlierEvents.Should().BeFalse();
        tree.Moments.Select(moment => (tree.Bots[moment.RunId], moment.Kind)).Should().Equal(
            (marshal, RunMomentKind.Step),
            (marshal, RunMomentKind.Delegation),
            (scout, RunMomentKind.Step),
            (scout, RunMomentKind.Tool),
            (scout, RunMomentKind.Step),
            (scout, RunMomentKind.Delegation),
            (checker, RunMomentKind.Step),
            (checker, RunMomentKind.Tool),
            (checker, RunMomentKind.Approval),
            (checker, RunMomentKind.Step),
            (scout, RunMomentKind.Answer),
            (scout, RunMomentKind.Step),
            (marshal, RunMomentKind.Answer),
            (marshal, RunMomentKind.Step),
            (marshal, RunMomentKind.Tool),
            (marshal, RunMomentKind.Approval),
            (marshal, RunMomentKind.Step));
    }

    [Fact]
    public void A_hand_off_names_the_bot_and_run_it_went_to_and_its_answer_comes_back_from_them()
    {
        var tree = RunTreeReducer.Apply(RunTreeView.Start(rootRun, marshal), Load());

        var handOff = tree.Moments.First(moment => moment.Kind is RunMomentKind.Delegation);
        handOff.Should().Match<RunMoment>(moment => moment.ChildBotId == scout && moment.ChildRunId == scoutRun && moment.EndedAt != null);
        var answer = tree.Moments.Last(moment => moment.Kind is RunMomentKind.Answer);
        answer.Should().Match<RunMoment>(moment =>
            moment.RunId == rootRun && moment.ToolCallId == handOff.Id && moment.ChildBotId == scout && moment.Outcome == "completed");
        answer.Text.Should().Contain("Three sources, checked by Checker");
        tree.Moments.Where(moment => moment.Kind is RunMomentKind.Approval).Should().OnlyContain(moment => moment.Outcome == "approved" && moment.EndedAt != null);
        tree.Moments.Where(moment => moment.Kind is RunMomentKind.Step).Should().OnlyContain(moment => moment.Cost != null && moment.EndedAt != null);
    }

    #endregion

    #region Order And Replay

    [Fact]
    public void Any_prefix_of_the_stream_and_then_the_rest_ends_where_the_whole_stream_at_once_does()
    {
        var log = Load();
        var whole = Snapshot(RunTreeReducer.Apply(RunTreeView.Start(rootRun, marshal), log));

        for (var split = 0; split <= log.Count; split++)
        {
            var start = RunTreeReducer.Apply(RunTreeView.Start(rootRun, marshal), log.Take(split));
            var resumed = RunTreeReducer.Apply(RunTreeReducer.Apply(start, log.Skip(Math.Max(split - 3, 0))), log.Skip(split));

            Snapshot(resumed).Should().Be(whole, $"a viewer that read {split} events, then got some of them again on reconnecting, ends where one that never left does");
        }
    }

    [Fact]
    public void A_log_cut_off_while_a_bot_waits_leaves_its_call_waiting_and_the_work_above_it_open()
    {
        var log = Load();
        var waitsFor = log.First(stored => stored.Type == RunEventTypes.ToolApprovalRequested).Seq;
        var tree = RunTreeReducer.Apply(RunTreeView.Start(rootRun, marshal), log.Where(stored => stored.Seq <= waitsFor + 2));

        tree.IsActive.Should().BeTrue();
        tree.Moments.Single(moment => moment.Kind is RunMomentKind.Approval).Should().Match<RunMoment>(moment => moment.Outcome == null && moment.EndedAt == null);
        tree.Moments.Where(moment => moment.Kind is RunMomentKind.Delegation).Should().HaveCount(2).And.OnlyContain(moment => moment.EndedAt == null);
        tree.RunOf(checkerRun).ToolCalls.Single().State.Should().Be("awaiting_approval", "a call that waits is still waiting, never stopped, while its run goes on");
        tree.RunOf(checkerRun).Status.Should().Be("waiting");
    }

    [Fact]
    public void A_stream_whose_first_events_are_no_longer_kept_says_so_and_a_run_whose_start_is_gone_has_no_bot()
    {
        var log = Load();
        var tree = RunTreeReducer.Apply(RunTreeView.Start(rootRun, marshal), log.Where(stored => stored.Seq > 20));

        tree.LostEarlierEvents.Should().BeTrue();
        tree.Bots.Should().NotContainKey(scoutRun);
        tree.Bots[checkerRun].Should().Be(checker);
    }

    [Fact]
    public void A_step_whose_model_call_was_cut_off_leaves_the_timeline()
    {
        var log = Load();
        var step = log.First(stored => stored.Type == RunEventTypes.StepStarted);
        var discarded = new RunEventResponse(step.Seq + 1, step.RunId, RunEventTypes.StepDiscarded, step.Data, step.CreatedAt);

        var tree = RunTreeReducer.Apply(RunTreeView.Start(rootRun, marshal), [.. log.Where(stored => stored.Seq <= step.Seq), discarded]);

        tree.Moments.Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private static List<RunEventResponse> Load() =>
        JsonSerializer.Deserialize<List<RunEventResponse>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", ThreeBots)), json)!;

    /// <summary>The whole tree as text, so two trees are compared by everything in them.</summary>
    private static string Snapshot(RunTreeView tree) =>
        JsonSerializer.Serialize(new { tree.LastSeq, tree.FirstSeq, Runs = tree.Runs.OrderBy(run => run.Key).ToList(), tree.Bots, tree.Moments }, json);

    #endregion
}
