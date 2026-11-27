namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Shapes a Copilot chat request by tightening tool schemas. Streaming and tool round-trips go through the shared
/// protocol client with the GitHub user token as bearer.
/// </summary>
internal sealed class CopilotChatClient(IModelChatClient inner) : IModelChatClient
{
    #region Interface Implementations

    public Task<ModelChatResponse> GetResponse(ModelChatRequest request, CancellationToken cancellation = default) =>
        inner.GetResponse(Shape(request), cancellation);

    public IAsyncEnumerable<ModelChatUpdate> GetStreamingResponse(
        ModelChatRequest request,
        CancellationToken cancellation = default) =>
        inner.GetStreamingResponse(Shape(request), cancellation);

    public void Dispose() => inner.Dispose();

    #endregion

    #region Private Methods

    private static ModelChatRequest Shape(ModelChatRequest request) =>
        request.Options.Tools is { Count: > 0 } tools
            ? request with { Options = request.Options with { Tools = [.. tools.Select(Tighten)] } }
            : request;

    private static ModelTool Tighten(ModelTool tool) =>
        new(tool.Name, tool.Description, CopilotToolSchema.Tighten(tool.Schema));

    #endregion
}
