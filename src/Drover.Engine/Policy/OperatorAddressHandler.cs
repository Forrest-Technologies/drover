namespace Drover.Engine.Policy;

/// <summary>
/// Marks every request of one client as going to an address the operator configured (<see cref="OutboundHttp.ForOperatorAddress"/>).
/// Added only to the client of a service the operator names in the server's configuration, never to a client whose
/// addresses come from a space, a person, or a bot.
/// </summary>
public sealed class OperatorAddressHandler : DelegatingHandler
{
    #region Public Methods

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        base.SendAsync(request.ForOperatorAddress(), cancellationToken);

    #endregion
}
