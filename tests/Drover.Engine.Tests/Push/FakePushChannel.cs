namespace Drover.Engine.Tests.Push;

/// <summary>A push service stand-in: it keeps every wake-up it is handed and answers what the test says.</summary>
public sealed class FakePushChannel : IPushChannel
{
    #region Private Fields

    private readonly ConcurrentQueue<(PushSubscription Device, PushWakeUp WakeUp)> sent = new();

    #endregion

    #region Properties

    public string Kind => "fake";

    /// <summary>The answer for each send, by how many came before it. Delivered when not set.</summary>
    public Func<int, PushOutcome> Answer { get; set; } = _ => PushOutcome.Delivered;

    public IReadOnlyList<(PushSubscription Device, PushWakeUp WakeUp)> Sent => [.. sent];

    #endregion

    #region Public Methods

    /// <summary>The wake-ups sent to one device, in order.</summary>
    public IReadOnlyList<PushWakeUp> SentTo(string endpoint) =>
        [.. sent.Where(push => push.Device.Endpoint == endpoint).Select(push => push.WakeUp)];

    #endregion

    #region Interface Implementations

    public Task<PushOutcome> Send(PushSubscription subscription, PushWakeUp wakeUp, CancellationToken cancellationToken)
    {
        TenantScope.IsOpenInCurrentFlow.Should().BeFalse("a push service is never asked inside a tenant transaction");
        var count = sent.Count;
        sent.Enqueue((subscription, wakeUp));
        return Task.FromResult(Answer(count));
    }

    #endregion
}
