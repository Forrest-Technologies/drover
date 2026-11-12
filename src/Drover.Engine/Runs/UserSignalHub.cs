namespace Drover.Engine.Runs;

/// <summary>
/// Hints for one person in one space that something of theirs changed. Nothing here is stored and nothing is
/// replayed: a signal only says "fetch again", so losing one costs a stale list until the next, never data.
/// </summary>
public sealed class UserSignalHub
{
    #region Private Fields

    private const int SubscriberCapacity = 64;

    private readonly ConcurrentDictionary<(Guid SpaceId, Guid UserId), ImmutableList<Channel<UserSignal>>> subscribers = new();

    #endregion

    #region Public Methods

    public UserSignalSubscription Subscribe(Guid spaceId, Guid userId)
    {
        var channel = Channel.CreateBounded<UserSignal>(
            new BoundedChannelOptions(SubscriberCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        var key = (spaceId, userId);
        subscribers.AddOrUpdate(key, _ => [channel], (_, existing) => existing.Add(channel));
        return new UserSignalSubscription(channel.Reader, () => Remove(key, channel));
    }

    public void Publish(Guid spaceId, Guid userId, UserSignal signal)
    {
        if (subscribers.TryGetValue((spaceId, userId), out var watching))
        {
            foreach (var channel in watching)
            {
                channel.Writer.TryWrite(signal);
            }
        }
    }

    #endregion

    #region Private Methods

    private void Remove((Guid SpaceId, Guid UserId) key, Channel<UserSignal> channel)
    {
        while (subscribers.TryGetValue(key, out var existing))
        {
            var remaining = existing.Remove(channel);
            var replaced = remaining.IsEmpty
                ? subscribers.TryRemove(new KeyValuePair<(Guid SpaceId, Guid UserId), ImmutableList<Channel<UserSignal>>>(key, existing))
                : subscribers.TryUpdate(key, remaining, existing);

            if (replaced)
            {
                break;
            }
        }

        channel.Writer.TryComplete();
    }

    #endregion
}


public sealed class UserSignalSubscription(ChannelReader<UserSignal> signals, Action unsubscribe) : IDisposable
{
    #region Properties

    public ChannelReader<UserSignal> Signals => signals;

    #endregion

    #region Interface Implementations

    public void Dispose() => unsubscribe();

    #endregion
}
