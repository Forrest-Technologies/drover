namespace Drover.Engine.Runs;

/// <summary>
/// Hands committed events to whoever is watching a run in this process. Events are published after their transaction
/// commits, so a subscriber never sees one that was rolled back. Delivery is a convenience, not the record: a
/// subscriber that falls behind loses the oldest events here and reads them from the table, because every event has
/// a gapless number and the table has them all.
/// </summary>
public sealed class RunEventHub
{
    #region Private Fields

    private const int SubscriberCapacity = 512;

    private readonly ConcurrentDictionary<Guid, ImmutableList<Channel<RunEventResponse>>> subscribers = new();

    #endregion

    #region Public Methods

    /// <summary>
    /// Subscribes to the events of one run tree. The caller has already checked that the person may see the run's
    /// thread; the hub holds no tenant data of its own to check against.
    /// </summary>
    public RunEventSubscription Subscribe(Guid rootRunId)
    {
        var channel = Channel.CreateBounded<RunEventResponse>(
            new BoundedChannelOptions(SubscriberCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        subscribers.AddOrUpdate(rootRunId, _ => [channel], (_, existing) => existing.Add(channel));
        return new RunEventSubscription(channel.Reader, () => Remove(rootRunId, channel));
    }

    public void Publish(Guid rootRunId, IReadOnlyList<RunEvent> events)
    {
        if (events.Count == 0 || !subscribers.TryGetValue(rootRunId, out var watching))
        {
            return;
        }

        foreach (var stored in events)
        {
            var response = stored.ToResponse();

            foreach (var channel in watching)
            {
                channel.Writer.TryWrite(response);
            }
        }
    }

    #endregion

    #region Private Methods

    private void Remove(Guid rootRunId, Channel<RunEventResponse> channel)
    {
        while (subscribers.TryGetValue(rootRunId, out var existing))
        {
            var remaining = existing.Remove(channel);
            var replaced = remaining.IsEmpty
                ? subscribers.TryRemove(new KeyValuePair<Guid, ImmutableList<Channel<RunEventResponse>>>(rootRunId, existing))
                : subscribers.TryUpdate(rootRunId, remaining, existing);

            if (replaced)
            {
                break;
            }
        }

        channel.Writer.TryComplete();
    }

    #endregion
}


/// <summary>One watcher's feed of live events. Disposing it stops the feed.</summary>
public sealed class RunEventSubscription(ChannelReader<RunEventResponse> events, Action unsubscribe) : IDisposable
{
    #region Properties

    public ChannelReader<RunEventResponse> Events => events;

    #endregion

    #region Interface Implementations

    public void Dispose() => unsubscribe();

    #endregion
}
