namespace Drover.Engine.Policy;

/// <summary>
/// States, for the outbound calls one provider adapter makes, which space they are for. An adapter builds its own
/// requests and knows nothing of policy, so the caller wraps the adapter call in <see cref="Begin"/> and the
/// <see cref="SpaceOutboundHandler"/> puts the space's effective rule on every request made inside it. A request
/// made outside any wrapper says nothing and is treated as forbidden from private addresses.
/// </summary>
public sealed class OutboundAttempt : IDisposable
{
    #region Private Fields

    private static readonly AsyncLocal<OutboundAttempt?> current = new();

    private readonly OutboundAttempt? previous;

    #endregion

    #region Constructors

    private OutboundAttempt(EffectivePolicy policy)
    {
        Policy = policy;
        previous = current.Value;
        current.Value = this;
    }

    #endregion

    #region Properties

    public static OutboundAttempt? Current => current.Value;

    public EffectivePolicy Policy { get; }

    #endregion

    #region Public Methods

    public static OutboundAttempt Begin(EffectivePolicy policy) => new(policy);

    public void Dispose() => current.Value = previous;

    #endregion
}

/// <summary>Applies the space's rule from the surrounding <see cref="OutboundAttempt"/> to each outbound request.</summary>
public sealed class SpaceOutboundHandler : DelegatingHandler
{
    #region Public Methods

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var attempt = OutboundAttempt.Current;

        if (attempt is not null)
        {
            request.ForSpace(attempt.Policy);
        }

        return base.SendAsync(request, cancellationToken);
    }

    #endregion
}
