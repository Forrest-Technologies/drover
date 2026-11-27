namespace Drover.Domain.Decisions;

/// <summary>
/// One kind of decision model connection, in the shape of <see cref="IModelClientProvider"/>: a kind, a client built
/// from an endpoint, and a test. Each adapter implements this and keeps every vendor type inside itself.
/// </summary>
public interface IDecisionModelProvider
{
    /// <summary>The connection kind this provider serves.</summary>
    string Kind { get; }

    /// <summary>The address every connection of this kind uses, or null when the connection supplies its own.</summary>
    Uri? FixedAddress => null;

    IDecisionModel CreateClient(ModelEndpoint endpoint, string? apiKey, string model);

    Task<ConnectionTestResult> TestConnection(ModelEndpoint endpoint, string? apiKey, string model, CancellationToken cancellationToken);
}
