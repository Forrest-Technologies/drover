namespace Drover.Domain.Models;

/// <summary>
/// Turns a model connection into a chat client and answers what the connection offers.
/// One implementation per connection kind. The key is passed in by the caller and never stored here.
/// </summary>
public interface IModelClientProvider
{
    /// <summary>The connection kind this provider serves, for example "venice".</summary>
    string Kind { get; }

    /// <summary>The address every connection of this kind uses, or null when the connection supplies its own.</summary>
    Uri? FixedAddress => null;

    /// <summary>Builds a chat client for one model. Provider failures surface as <see cref="ModelProviderException"/>.</summary>
    IModelChatClient CreateChatClient(ModelEndpoint connection, string? apiKey, string modelId, ModelSettings settings);

    Task<IReadOnlyList<ModelInfo>> ListModels(ModelEndpoint connection, string? apiKey, CancellationToken cancellation);

    Task<ConnectionTestResult> TestConnection(ModelEndpoint connection, string? apiKey, CancellationToken cancellation);
}
