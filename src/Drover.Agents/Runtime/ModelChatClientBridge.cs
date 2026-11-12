namespace Drover.Agents.Runtime;

/// <summary>Adapts Drover's provider seam to the framework without moving orchestration out of the framework.</summary>
public sealed class ModelChatClientBridge(IModelChatClient client) : IChatClient
{
    #region Interface Implementations

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        ModelChatMapping.ToChatResponse(await client.GetResponse(ModelChatMapping.ToModelRequest(messages, options), cancellationToken));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in client.GetStreamingResponse(ModelChatMapping.ToModelRequest(messages, options), cancellationToken))
        {
            yield return ModelChatMapping.ToChatUpdate(update);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() => client.Dispose();

    #endregion
}
