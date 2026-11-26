namespace Drover.Adapters.OpenAICompatible;

/// <summary>Puts Copilot's required headers on every request the named HTTP clients send.</summary>
internal sealed class CopilotHeaderHandler(IOptions<CopilotPlanOptions> options) : DelegatingHandler
{
    #region Interface Implementations

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CopilotHeaders.Apply(request, options.Value);
        return base.SendAsync(request, cancellationToken);
    }

    #endregion
}
