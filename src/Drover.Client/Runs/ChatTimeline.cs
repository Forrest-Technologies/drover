namespace Drover.Client.Runs;

/// <summary>
/// The messages a chat screen shows: the thread's stored messages, then whatever the active run has produced that is
/// not stored yet. A message the run completed is in both; it appears once, as stored.
/// </summary>
public static class ChatTimeline
{
    #region Public Methods

    public static ImmutableList<MessageView> Merge(IEnumerable<MessageResponse> stored, RunView? run)
    {
        ImmutableList<MessageView> timeline = [.. stored.OrderBy(message => message.Position).Select(MessageView.From)];

        if (run is null)
        {
            return timeline;
        }

        var known = timeline.Select(message => message.Id).ToHashSet();
        return timeline.AddRange(run.Messages.Where(message => !known.Contains(message.Id)));
    }

    #endregion
}
