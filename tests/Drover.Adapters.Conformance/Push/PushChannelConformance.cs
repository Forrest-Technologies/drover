namespace Drover.Adapters.Conformance.Push;

/// <summary>How the push service answers in a scenario. The adapter's test turns each into its service's real answer.</summary>
public enum PushServiceAnswer
{
    Accepted,
    Gone,
    NotFound,
    TooManyRequests,
    Unavailable,
    Refused,
    Unreachable,

    /// <summary>This server's outbound policy refuses the address before any request leaves.</summary>
    Blocked,
}


/// <summary>What reached the push service: its headers, and the payload as the device would read it after decrypting.</summary>
public sealed record ReceivedPush(IReadOnlyDictionary<string, string> Headers, string Payload);


/// <summary>A channel aimed at a push service that answers as asked, a subscription it can address, and what the service received.</summary>
public sealed record PushRig(IPushChannel Channel, PushSubscription Subscription, Func<IReadOnlyList<ReceivedPush>> Received);


/// <summary>
/// What every <see cref="IPushChannel"/> must do. An adapter inherits this class and builds a rig for each answer. The
/// suite holds the one promise the design rests on: a push service is told the kind and the thread, and nothing else.
/// </summary>
public abstract class PushChannelConformance
{
    #region Private Fields

    private static readonly PushWakeUp wakeUp = new(PushKinds.ApprovalWaiting, Guid.CreateVersion7());

    #endregion

    #region Properties

    /// <summary>Header names a channel may send. None of them can carry a thread, a bot, or a word of the conversation.</summary>
    protected virtual IReadOnlySet<string> AllowedHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Crypto-Key", "Content-Encoding", "Content-Type", "Content-Length", "TTL", "Urgency",
    };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #endregion

    #region Delivery

    [Fact]
    public void The_channel_has_a_kind()
    {
        Rig(PushServiceAnswer.Accepted).Channel.Kind.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task An_accepted_push_is_delivered()
    {
        var rig = Rig(PushServiceAnswer.Accepted);

        (await rig.Channel.Send(rig.Subscription, wakeUp, Token)).Should().Be(PushOutcome.Delivered);
        rig.Received().Should().ContainSingle();
    }

    [Fact]
    public async Task The_payload_is_the_kind_and_the_thread_and_nothing_else()
    {
        var rig = Rig(PushServiceAnswer.Accepted);

        await rig.Channel.Send(rig.Subscription, wakeUp, Token);

        var received = rig.Received().Should().ContainSingle().Subject;
        using var payload = JsonDocument.Parse(received.Payload);
        payload.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(["kind", "thread"]);
        payload.RootElement.GetProperty("kind").GetString().Should().Be(wakeUp.Kind);
        payload.RootElement.GetProperty("thread").GetGuid().Should().Be(wakeUp.ThreadId);
    }

    [Fact]
    public async Task No_header_carries_anything_about_the_conversation()
    {
        var rig = Rig(PushServiceAnswer.Accepted);

        await rig.Channel.Send(rig.Subscription, wakeUp, Token);

        var received = rig.Received().Should().ContainSingle().Subject;
        received.Headers.Keys.Should().OnlyContain(name => AllowedHeaders.Contains(name));
        received.Headers.Values.Should().NotContain(value => value.Contains(wakeUp.ThreadId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Failures

    [Theory]
    [InlineData(PushServiceAnswer.Gone)]
    [InlineData(PushServiceAnswer.NotFound)]
    [InlineData(PushServiceAnswer.Refused)]
    [InlineData(PushServiceAnswer.Blocked)]
    public async Task A_subscription_the_service_will_never_deliver_to_is_gone(PushServiceAnswer answer)
    {
        var rig = Rig(answer);

        (await rig.Channel.Send(rig.Subscription, wakeUp, Token)).Should().Be(PushOutcome.SubscriptionGone);
    }

    [Theory]
    [InlineData(PushServiceAnswer.TooManyRequests)]
    [InlineData(PushServiceAnswer.Unavailable)]
    [InlineData(PushServiceAnswer.Unreachable)]
    public async Task A_service_that_is_busy_down_or_unreachable_is_tried_again_later(PushServiceAnswer answer)
    {
        var rig = Rig(answer);

        (await rig.Channel.Send(rig.Subscription, wakeUp, Token)).Should().Be(PushOutcome.RetryLater);
    }

    [Fact]
    public async Task A_device_whose_key_is_not_a_point_on_the_curve_is_gone_and_nothing_is_sent()
    {
        var rig = Rig(PushServiceAnswer.Accepted);
        byte[] offTheCurve = [0x04, .. new byte[64]];
        rig.Subscription.PublicKey = Convert.ToBase64String(offTheCurve).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        (await rig.Channel.Send(rig.Subscription, wakeUp, Token)).Should().Be(PushOutcome.SubscriptionGone);
        rig.Received().Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_throws_and_is_not_an_outcome()
    {
        var rig = Rig(PushServiceAnswer.Accepted);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var send = () => rig.Channel.Send(rig.Subscription, wakeUp, cancelled.Token);

        await send.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Rig

    /// <summary>A channel whose push service answers with <paramref name="answer"/>, offline.</summary>
    protected abstract PushRig Rig(PushServiceAnswer answer);

    #endregion
}
