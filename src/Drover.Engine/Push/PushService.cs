namespace Drover.Engine.Push;

/// <summary>
/// A person's devices, their choice of which bots may notify them, and what a woken device shows. A device is the
/// person's in every space, so its transactions set the person and no space. A device's text is read in each space
/// of the person in turn, as that person, so a thread they cannot see is never described.
/// </summary>
public sealed class PushService(
    PushRepository push,
    BotRepository bots,
    TenantScope scope,
    TenantContext tenant,
    AccountRepository accounts,
    RunScopes scopes,
    TimeProvider time)
{
    #region Private Fields

    /// <summary>
    /// How many devices one person may have woken. Each wake-up is a request from this server to an address a device
    /// handed in, so the number is bounded; a new device beyond it replaces the oldest.
    /// </summary>
    public const int MaxDevicesPerPerson = 10;

    #endregion

    #region Devices

    /// <summary>Turns the device on for the person. False when what the browser gave is not a push subscription.</summary>
    public async Task<bool> TurnOn(Guid userId, PushDeviceRequest device, CancellationToken cancellationToken)
    {
        if (!IsSubscription(device))
        {
            return false;
        }

        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        await push.SaveSubscription(
            new PushSubscription
            {
                UserId = userId,
                Endpoint = device.Endpoint,
                PublicKey = device.PublicKey,
                AuthSecret = device.AuthSecret,
                CreatedAt = time.GetUtcNow(),
            },
            MaxDevicesPerPerson,
            cancellationToken);
        await work.Commit(cancellationToken);
        return true;
    }

    public async Task TurnOff(Guid userId, string endpoint, CancellationToken cancellationToken)
    {
        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        await push.RemoveSubscription(endpoint, cancellationToken);
        await work.Commit(cancellationToken);
    }

    public async Task<bool> IsOn(Guid userId, string endpoint, CancellationToken cancellationToken)
    {
        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        var on = await push.HasSubscription(endpoint, cancellationToken);
        await work.Commit(cancellationToken);
        return on;
    }

    #endregion

    #region Bots

    /// <summary>Whether the bot may notify the person, or null when the bot is not one they can see.</summary>
    public async Task<bool?> Notifies(Guid spaceId, Guid botId, Guid userId, CancellationToken cancellationToken)
    {
        tenant.Set(spaceId, userId);
        await using var work = await scope.Begin(cancellationToken);
        bool? notifies = await bots.FindBot(botId, cancellationToken) is null
            ? null
            : !await push.IsMuted(botId, userId, cancellationToken);
        await work.Commit(cancellationToken);
        return notifies;
    }

    /// <summary>Lets the bot notify the person, or stops it. False when the bot is not one they can see.</summary>
    public async Task<bool> SetNotifies(Guid spaceId, Guid botId, Guid userId, bool notifies, CancellationToken cancellationToken)
    {
        tenant.Set(spaceId, userId);
        await using var work = await scope.Begin(cancellationToken);

        if (await bots.FindBot(botId, cancellationToken) is null)
        {
            return false;
        }

        if (notifies)
        {
            await push.Unmute(botId, userId, cancellationToken);
        }
        else
        {
            await push.Mute(spaceId, botId, userId, time.GetUtcNow(), cancellationToken);
        }

        await work.Commit(cancellationToken);
        return true;
    }

    #endregion

    #region Wake Text

    /// <summary>
    /// What a device woken with <paramref name="kind"/> about <paramref name="threadId"/> shows, or null when no space
    /// of the person holds a thread of that id they can see, or the kind is not one Drover sends.
    /// </summary>
    public async Task<PushWakeText?> Describe(Guid userId, string kind, Guid threadId, CancellationToken cancellationToken)
    {
        if (!PushWords.Knows(kind))
        {
            return null;
        }

        foreach (var space in await ListSpaces(userId, cancellationToken))
        {
            var found = await scopes.For(space.SpaceId, userId).Transact(
                services => FindThreadAndBot(services, threadId, cancellationToken),
                cancellationToken);

            if (found is var (thread, bot))
            {
                return new PushWakeText(PushWords.Sentence(kind, bot.Name)!, bot.Id, thread.Id, thread.IsMain);
            }
        }

        return null;
    }

    #endregion

    #region Private Methods

    private async Task<IReadOnlyList<SpaceMembership>> ListSpaces(Guid userId, CancellationToken cancellationToken)
    {
        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        var memberships = await accounts.ListMemberships(cancellationToken);
        await work.Commit(cancellationToken);
        return memberships;
    }

    private static async Task<(ChatThread Thread, Bot Bot)?> FindThreadAndBot(IServiceProvider services, Guid threadId, CancellationToken cancellationToken)
    {
        var thread = await services.GetRequiredService<ThreadRepository>().FindThread(threadId, cancellationToken);
        var bot = thread is null ? null : await services.GetRequiredService<BotRepository>().FindBot(thread.BotId, cancellationToken);
        return thread is not null && bot is not null ? (thread, bot) : null;
    }

    /// <summary>An https push service address and keys of the sizes Web Push uses: a P-256 public key and a 16-byte secret.</summary>
    private static bool IsSubscription(PushDeviceRequest device) =>
        Uri.TryCreate(device.Endpoint, UriKind.Absolute, out var endpoint)
        && endpoint.Scheme == Uri.UriSchemeHttps
        && device.Endpoint.Length <= 2048
        && DecodedLength(device.PublicKey) == 65
        && DecodedLength(device.AuthSecret) == 16;

    private static int DecodedLength(string value) =>
        Base64Url.IsValid(value, out var length) ? length : -1;

    #endregion
}
