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
    TimeProvider time,
    IEnumerable<IPushChannel> channels)
{
    #region Private Fields

    /// <summary>
    /// How many devices one person may have woken. Each wake-up is a request from this server to an address a device
    /// handed in, so the number is bounded; a new device beyond it replaces the oldest.
    /// </summary>
    public const int MaxDevicesPerPerson = 10;

    /// <summary>The prime of the P-256 field, and the constant b of its curve y² = x³ − 3x + b.</summary>
    private static readonly BigInteger curvePrime = BigInteger.Parse("0FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF", NumberStyles.HexNumber);

    private static readonly BigInteger curveB = BigInteger.Parse("05AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B", NumberStyles.HexNumber);

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

    /// <summary>
    /// Puts the device's new subscription in the place of the one it had at <paramref name="previousEndpoint"/>, in one
    /// transaction for one person: only when that previous subscription is the person's. The person's own row of it is
    /// the proof, read under the same identity that writes the new one, so a request can never move a subscription
    /// another account held.
    /// </summary>
    public async Task<PushMove> Move(Guid userId, string previousEndpoint, PushDeviceRequest device, CancellationToken cancellationToken)
    {
        if (!IsSubscription(device))
        {
            return PushMove.NotASubscription;
        }

        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);

        if (!await push.HasSubscription(previousEndpoint, cancellationToken))
        {
            return PushMove.NotThePersons;
        }

        await push.RemoveSubscription(previousEndpoint, cancellationToken);
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
        return PushMove.Moved;
    }

    public async Task TurnOff(Guid userId, string endpoint, CancellationToken cancellationToken)
    {
        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        await push.RemoveSubscription(endpoint, cancellationToken);
        await work.Commit(cancellationToken);
    }

    public async Task<PushOutcome?> Test(Guid userId, string endpoint, CancellationToken cancellationToken)
    {
        if (channels.FirstOrDefault() is not { } channel)
        {
            return PushOutcome.RetryLater;
        }

        tenant.SetUser(userId);
        PushSubscription? subscription;
        await using (var work = await scope.Begin(cancellationToken))
        {
            subscription = await push.FindSubscription(endpoint, cancellationToken);
            await work.Commit(cancellationToken);
        }

        if (subscription is null)
        {
            return null;
        }

        var outcome = await channel.Send(subscription, new PushWakeUp(PushKinds.DeviceTest, Guid.Empty), cancellationToken);
        if (outcome is PushOutcome.SubscriptionGone)
        {
            await TurnOff(userId, endpoint, cancellationToken);
        }

        return outcome;
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
                return new PushWakeText(PushWords.Sentence(kind, bot.Name)!, bot.Id, thread.Id, thread.IsMain, space.SpaceId);
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

    /// <summary>
    /// An https push service address that is not plainly a private one, a P-256 public key that is a point on the
    /// curve, and a 16-byte secret. A key off the curve could never have a payload encrypted to it. A host name that
    /// only resolves to a private address is refused when the push is sent, by the outbound client.
    /// </summary>
    private static bool IsSubscription(PushDeviceRequest device) =>
        Uri.TryCreate(device.Endpoint, UriKind.Absolute, out var endpoint)
        && endpoint.Scheme == Uri.UriSchemeHttps
        && device.Endpoint.Length <= 2048
        && !IsPlainlyPrivate(endpoint)
        && IsPointOnTheCurve(device.PublicKey)
        && DecodedLength(device.AuthSecret) == 16;

    private static bool IsPlainlyPrivate(Uri endpoint) =>
        endpoint.IsLoopback || (IPAddress.TryParse(endpoint.DnsSafeHost, out var address) && PrivateAddresses.Contains(address));

    private static bool IsPointOnTheCurve(string publicKey)
    {
        if (DecodedLength(publicKey) != 65)
        {
            return false;
        }

        var point = Base64Url.DecodeFromChars(publicKey);

        if (point[0] != 0x04)
        {
            return false;
        }

        var x = new BigInteger(point.AsSpan(1, 32), isUnsigned: true, isBigEndian: true);
        var y = new BigInteger(point.AsSpan(33, 32), isUnsigned: true, isBigEndian: true);
        return x < curvePrime && y < curvePrime
            && BigInteger.Remainder((y * y) - (BigInteger.ModPow(x, 3, curvePrime) - (3 * x) + curveB), curvePrime).IsZero;
    }

    private static int DecodedLength(string value) =>
        Base64Url.IsValid(value, out var length) ? length : -1;

    #endregion
}
