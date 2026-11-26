namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Shapes a Copilot chat request: tightens tool schemas, and sets the initiator and vision headers for the
/// duration of the call. Streaming and tool round-trips go through the shared protocol client.
/// </summary>
internal sealed class CopilotChatClient(IModelChatClient inner) : IModelChatClient
{
    #region Interface Implementations

    public async Task<ModelChatResponse> GetResponse(ModelChatRequest request, CancellationToken cancellation = default)
    {
        CopilotCall.Current = CopilotCall.From(request);
        try
        {
            return await inner.GetResponse(Shape(request), cancellation);
        }
        finally
        {
            CopilotCall.Current = null;
        }
    }

    public async IAsyncEnumerable<ModelChatUpdate> GetStreamingResponse(
        ModelChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        CopilotCall.Current = CopilotCall.From(request);
        try
        {
            await foreach (var update in inner.GetStreamingResponse(Shape(request), cancellation))
            {
                yield return update;
            }
        }
        finally
        {
            CopilotCall.Current = null;
        }
    }

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
