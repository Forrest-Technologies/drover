namespace Drover.Client.Runs;

/// <summary>
/// The messages a chat screen shows: the conversation as stored, then whatever the active run has produced or taken
/// that the stored list does not have in place yet, then the messages still pending. A message appears once: one
/// the run completed as stored, and one that was pending when the list was fetched where the run delivered it.
/// </summary>
public static class ChatTimeline
{
    #region Public Methods

    public static ImmutableList<MessageView> Merge(IEnumerable<MessageResponse> stored, RunView? run)
    {
        List<MessageResponse> all = [.. stored];
        ImmutableList<MessageView> timeline =
        [
            .. all.Where(message => message.Position is not null).OrderBy(message => message.Position).Select(MessageView.From),
        ];
        var known = timeline.Select(message => message.Id).ToHashSet();
        var fromRun = (run?.Messages ?? []).Where(message => known.Add(message.Id)).ToList();
        return timeline
            .AddRange(fromRun)
            .AddRange(all.Where(message => message.Position is null && known.Add(message.Id)).Select(MessageView.From));
    }

    #endregion
}
