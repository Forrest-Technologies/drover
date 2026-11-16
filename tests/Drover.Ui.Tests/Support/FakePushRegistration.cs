namespace Drover.Ui.Tests.Support;

/// <summary>A browser stand-in: what it supports, the subscription it holds, and what it answers when asked.</summary>
public sealed class FakePushRegistration : IPushRegistration
{
    #region Properties

    public DevicePushSupport Support { get; set; } = DevicePushSupport.Available;

    /// <summary>What the browser reports once the person was asked and did not allow it.</summary>
    public DevicePushSupport SupportAfterAsking { get; set; } = DevicePushSupport.Available;

    public string? Endpoint { get; set; }

    /// <summary>What the browser gives when permission is granted. Null means it was not.</summary>
    public PushDeviceRequest? Subscription { get; set; }

    public string? AskedWith { get; private set; }

    /// <summary>The subscriptions ended, in order.</summary>
    public List<string> Unsubscribed { get; } = [];

    /// <summary>Checks never finish, as when the service worker never became ready, until the caller gives up.</summary>
    public bool CheckNeverFinishes { get; set; }

    /// <summary>The next check fails as a browser call does.</summary>
    public bool FailNextCheck { get; set; }

    #endregion

    #region Interface Implementations

    public async Task<DevicePushCheck> Check(CancellationToken cancellationToken = default)
    {
        if (CheckNeverFinishes)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (FailNextCheck)
        {
            FailNextCheck = false;
            throw new JSException("Sample: the browser call failed.");
        }

        return new DevicePushCheck(Support, Endpoint);
    }

    public Task<PushDeviceRequest?> Subscribe(string publicKey, CancellationToken cancellationToken = default)
    {
        AskedWith = publicKey;

        if (Subscription is null)
        {
            Support = SupportAfterAsking;
            return Task.FromResult<PushDeviceRequest?>(null);
        }

        Endpoint = Subscription.Endpoint;
        return Task.FromResult<PushDeviceRequest?>(Subscription);
    }

    public Task<string?> Unsubscribe(CancellationToken cancellationToken = default)
    {
        var ended = Endpoint;

        if (ended is not null)
        {
            Unsubscribed.Add(ended);
        }

        Endpoint = null;
        return Task.FromResult(ended);
    }

    #endregion
}
