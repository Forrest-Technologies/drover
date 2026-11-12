namespace Drover.Domain.Models;

/// <summary>The model transport seam. Agent orchestration and tool execution remain in Drover.Agents.</summary>
public interface IModelChatClient : IDisposable
{
    Task<ModelChatResponse> GetResponse(ModelChatRequest request, CancellationToken cancellation = default);

    IAsyncEnumerable<ModelChatUpdate> GetStreamingResponse(ModelChatRequest request, CancellationToken cancellation = default);
}

/// <summary>Builds the shared compatible protocol transport without coupling provider adapter projects.</summary>
public delegate IModelChatClient CompatibleChatClientBuilder(ModelEndpoint endpoint, string? apiKey, string modelId);
