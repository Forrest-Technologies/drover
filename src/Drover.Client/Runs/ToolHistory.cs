namespace Drover.Client.Runs;

/// <summary>
/// The tool calls of a thread's past runs, rebuilt from their logs by <see cref="RunReducer"/>, so a call shows the
/// same after a reload as it did live. Calls are found by their id, which is also the id of the message part that
/// made the call.
/// </summary>
public static class ToolHistory
{
    #region Public Methods

    /// <summary>
    /// The calls in a thread's tool events, as <c>ListToolEvents</c> returns them. The events of
    /// <paramref name="activeRunId"/>, the run still working, are left out: it is watched, and its own view is newer.
    /// A run that is over has stopped every call that was still open in its log, such as one that waited for an
    /// answer when the run was cancelled.
    /// </summary>
    public static ImmutableDictionary<Guid, ToolCallView> From(IEnumerable<RunEventResponse> events, Guid? activeRunId = null) =>
        events
            .Where(stored => stored.RunId != activeRunId)
            .GroupBy(stored => stored.RunId)
            .SelectMany(run => RunReducer.Apply(RunView.Start(run.Key), run.OrderBy(stored => stored.Seq)).ToolCalls)
            .Select(tool => tool.Settled())
            .ToImmutableDictionary(tool => tool.ToolCallId);

    /// <summary>The history with a run's calls added, or replaced where the history had them already.</summary>
    public static ImmutableDictionary<Guid, ToolCallView> With(this ImmutableDictionary<Guid, ToolCallView> history, RunView? run) =>
        run is null ? history : history.SetItems(run.ToolCalls.Select(tool => KeyValuePair.Create(tool.ToolCallId, tool)));

    #endregion
}
