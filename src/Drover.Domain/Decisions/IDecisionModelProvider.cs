namespace Drover.Domain.Decisions;

/// <summary>
/// One kind of decision model connection, in the shape of <see cref="IModelClientProvider"/>: a kind, a client built
/// from an endpoint, and a test. Each adapter implements this and keeps every vendor type inside itself.
/// </summary>
public interface IDecisionModelProvider
{
    /// <summary>The connection kind this provider serves.</summary>
    string Kind { get; }

    /// <summary>The provider's name as a person reads it.</summary>
    string DisplayName { get; }

    /// <summary>Whether a connection of this kind needs a key, may have one, or takes none.</summary>
    ModelKeyRule KeyRule { get; }

    /// <summary>What a connection of this kind gives to say where its provider is.</summary>
    DecisionAddressRule AddressRule { get; }

    /// <summary>The address every connection of this kind uses, or null when the connection supplies its own.</summary>
    Uri? FixedAddress => null;

    /// <summary>
    /// The address a connection's calls go to: the fixed one, the one the connection gives, or the one its account
    /// leads to. Null when what the rule needs is missing or is not an address.
    /// </summary>
    Uri? AddressFor(string? address, string? account);

    IDecisionModel CreateClient(ModelEndpoint endpoint, string? apiKey, string model);

    Task<ConnectionTestResult> TestConnection(ModelEndpoint endpoint, string? apiKey, string model, CancellationToken cancellationToken);
}
