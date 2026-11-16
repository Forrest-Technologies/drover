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
    /// answer when the run was cancelled. The events of a run that a call handed work to are applied under that call,
    /// as a live stream applies them; they always come after the call's <c>delegation.started</c>.
    /// </summary>
    public static ImmutableDictionary<Guid, ToolCallView> From(IEnumerable<RunEventResponse> events, Guid? activeRunId = null)
    {
        List<RunView> roots = [];

        foreach (var stored in events)
        {
            var index = roots.FindIndex(root => root.Holds(stored.RunId));

            if (index < 0)
            {
                roots.Add(RunReducer.Apply(RunView.Start(stored.RunId), stored));
            }
            else
            {
                roots[index] = RunReducer.Apply(roots[index], stored);
            }
        }

        return roots
            .Where(root => root.RunId != activeRunId)
            .SelectMany(root => root.ToolCalls)
            .Select(tool => tool.Settled())
            .ToImmutableDictionary(tool => tool.ToolCallId);
    }

    /// <summary>The history with a run's calls added, or replaced where the history had them already.</summary>
    public static ImmutableDictionary<Guid, ToolCallView> With(this ImmutableDictionary<Guid, ToolCallView> history, RunView? run) =>
        run is null ? history : history.SetItems(run.ToolCalls.Select(tool => KeyValuePair.Create(tool.ToolCallId, tool)));

    #endregion
}
