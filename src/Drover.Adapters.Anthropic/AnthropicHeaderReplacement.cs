namespace Drover.Adapters.Anthropic;

/// <summary>
/// Puts a destination's own headers on every request of a client in place of any value the client set for the same
/// name, so a configured <c>user-agent</c> is the only one sent rather than one more product after the client's own.
/// </summary>
internal sealed class AnthropicHeaderReplacement(IReadOnlyDictionary<string, string> headers) : DelegatingHandler
{
    #region Public Methods

    /// <summary>Removes every value of each named header from <paramref name="request"/>, then adds the given one.</summary>
    public static void Replace(HttpRequestMessage request, IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (name, value) in headers)
        {
            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    #endregion

    #region Interface Implementations

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Replace(request, headers);
        return base.SendAsync(request, cancellationToken);
    }

    #endregion
}
