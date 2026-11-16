namespace Drover.Data.Repositories;

/// <summary>
/// A person's devices that may be woken, and the bots they have asked not to notify them. Every method runs inside a
/// tenant scope the service has opened with the person set: the policies show a person only their own devices and
/// their own choices.
/// </summary>
public sealed class PushRepository(DroverDbContext db)
{
    #region Devices

    /// <summary>
    /// Adds the device, or renews its keys when the person registered it before. A person keeps at most
    /// <paramref name="keep"/> devices: the one registered longest ago makes room for a new one.
    /// </summary>
    public async Task SaveSubscription(PushSubscription subscription, int keep, CancellationToken cancellationToken)
    {
        var existing = await db.PushSubscriptions.SingleOrDefaultAsync(
            row => row.Endpoint == subscription.Endpoint,
            cancellationToken);

        if (existing is null)
        {
            await db.PushSubscriptions
                .OrderByDescending(row => row.CreatedAt)
                .ThenByDescending(row => row.Id)
                .Skip(keep - 1)
                .ExecuteDeleteAsync(cancellationToken);
            db.PushSubscriptions.Add(subscription);
            return;
        }

        existing.PublicKey = subscription.PublicKey;
        existing.AuthSecret = subscription.AuthSecret;
    }

    public async Task<IReadOnlyList<PushSubscription>> ListSubscriptions(CancellationToken cancellationToken) =>
        await db.PushSubscriptions.AsNoTracking().OrderBy(row => row.CreatedAt).ToListAsync(cancellationToken);

    public async Task<bool> HasSubscription(string endpoint, CancellationToken cancellationToken) =>
        await db.PushSubscriptions.AnyAsync(row => row.Endpoint == endpoint, cancellationToken);

    public Task RemoveSubscription(string endpoint, CancellationToken cancellationToken) =>
        db.PushSubscriptions.Where(row => row.Endpoint == endpoint).ExecuteDeleteAsync(cancellationToken);

    public Task RemoveSubscriptions(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.PushSubscriptions.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync(cancellationToken);

    #endregion

    #region Bots

    public async Task<bool> IsMuted(Guid botId, Guid userId, CancellationToken cancellationToken) =>
        await db.BotPushMutes.AnyAsync(row => row.BotId == botId && row.AudienceUserId == userId, cancellationToken);

    public async Task Mute(Guid spaceId, Guid botId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!await IsMuted(botId, userId, cancellationToken))
        {
            db.BotPushMutes.Add(new BotPushMute { SpaceId = spaceId, BotId = botId, AudienceUserId = userId, MutedAt = now });
        }
    }

    public Task Unmute(Guid botId, Guid userId, CancellationToken cancellationToken) =>
        db.BotPushMutes.Where(row => row.BotId == botId && row.AudienceUserId == userId).ExecuteDeleteAsync(cancellationToken);

    #endregion
}
