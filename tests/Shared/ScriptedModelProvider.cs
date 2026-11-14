namespace Drover.Tests.Common;

/// <summary>
/// A model provider that plays a <see cref="ScriptedModel"/> through Drover's own model seam, so a test runs the real
/// engine and the real agent runtime with no network. It stands for the provider, outside any Drover process: one
/// instance outlives a host that is killed and restarted. It remembers the tools every call was offered and whether
/// a tenant transaction was open in the calling flow, which must never be the case.
/// </summary>
public sealed class ScriptedModelProvider(ScriptedModel model) : IModelClientProvider
{
    #region Private Fields

    public const string ConnectionKind = "scripted_model";

    private readonly ConcurrentQueue<IReadOnlyList<string>> toolsOffered = new();

    private readonly ConcurrentQueue<bool> transactionWasOpen = new();

    private readonly ConcurrentQueue<string?> keys = new();

    private readonly ConcurrentQueue<IReadOnlyList<ModelData>> imagesSeen = new();

    private int calls;

    #endregion

    #region Properties

    public string Kind => ConnectionKind;

    public Uri? FixedAddress { get; } = new("https://scripted-model.example.test/v1/");

    public ScriptedModel Model => model;

    /// <summary>The tool names each model call was offered, in call order.</summary>
    public IReadOnlyList<IReadOnlyList<string>> ToolsOffered => [.. toolsOffered];

    /// <summary>For each model call, whether a tenant transaction was open in the flow that made it.</summary>
    public IReadOnlyList<bool> TransactionWasOpen => [.. transactionWasOpen];

    public IReadOnlyList<string?> KeysSeen => [.. keys];

    /// <summary>The images each model call was sent, in call order, with their bytes.</summary>
    public IReadOnlyList<IReadOnlyList<ModelData>> ImagesSeen => [.. imagesSeen];

    /// <summary>Runs at the start of each model call, with the call's number from zero, before the model answers.</summary>
    public Func<int, Task> BeforeCall { get; set; } = _ => Task.CompletedTask;

    /// <summary>A failure the next model call throws in place of an answer, once.</summary>
    public ModelProviderException? FailNext { get; set; }

    #endregion

    #region Interface Implementations

    public IModelChatClient CreateChatClient(ModelEndpoint connection, string? apiKey, string modelId, ModelSettings settings)
    {
        keys.Enqueue(apiKey);
        return new Client(this);
    }

    public Task<IReadOnlyList<ModelInfo>> ListModels(ModelEndpoint connection, string? apiKey, CancellationToken cancellation) =>
        Task.FromResult<IReadOnlyList<ModelInfo>>([]);

    public Task<ConnectionTestResult> TestConnection(ModelEndpoint connection, string? apiKey, CancellationToken cancellation) =>
        Task.FromResult(ConnectionTestResult.Ok("Connected."));

    #endregion

    #region Nested Types

    private sealed class Client(ScriptedModelProvider provider) : IModelChatClient
    {
        #region Interface Implementations

        public async Task<ModelChatResponse> GetResponse(ModelChatRequest request, CancellationToken cancellation = default)
        {
            List<ModelContent> contents = [];

            await foreach (var update in GetStreamingResponse(request, cancellation))
            {
                contents.AddRange(update.Contents);
            }

            return new ModelChatResponse([new ModelChatMessage("assistant", contents)]);
        }

        public async IAsyncEnumerable<ModelChatUpdate> GetStreamingResponse(
            ModelChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellation = default)
        {
            provider.transactionWasOpen.Enqueue(TenantScope.IsOpenInCurrentFlow);
            provider.toolsOffered.Enqueue([.. (request.Options.Tools ?? []).Select(tool => tool.Name)]);
            provider.imagesSeen.Enqueue([.. request.Messages.SelectMany(message => message.Contents).OfType<ModelData>()]);
            await provider.BeforeCall(Interlocked.Increment(ref provider.calls) - 1);

            if (provider.FailNext is { } failure)
            {
                provider.FailNext = null;
                throw failure;
            }

            var turn = provider.Model.NextTurn([.. request.Messages.SelectMany(Seen)]);

            foreach (var output in turn)
            {
                if (output is PauseOutput)
                {
                    provider.Model.SignalPaused();
                    await Task.Delay(Timeout.Infinite, cancellation);
                }

                yield return new ModelChatUpdate([ToContent(output)]) { Role = "assistant" };
            }

            yield return new ModelChatUpdate([new ModelTokenUsage(10, 5, 15)])
            {
                Role = "assistant",
                FinishReason = turn.Any(output => output is ToolCallOutput) ? "tool_calls" : "stop",
            };
        }

        public void Dispose()
        {
        }

        #endregion

        #region Private Methods

        private static ModelContent ToContent(ModelOutput output) => output switch
        {
            TextOutput text => new ModelText(text.Text),
            ToolCallOutput call => new ModelToolCall(call.CallId, call.ToolName, JsonElement.Parse(call.ArgumentsJson)),
            _ => throw new InvalidOperationException($"Unknown output {output}."),
        };

        private static IEnumerable<SeenMessage> Seen(ModelChatMessage message)
        {
            foreach (var content in message.Contents)
            {
                var summary = content switch
                {
                    ModelText text => text.Text,
                    ModelToolCall call => $"call {call.CallId} {call.Name}",
                    ModelToolResult result => $"{result.CallId} = {JsonSerializer.Serialize(result.Result)}",
                    ModelData data => $"image {data.MediaType}",
                    _ => null,
                };

                if (summary is not null)
                {
                    yield return new SeenMessage(message.Role, summary);
                }
            }
        }

        #endregion
    }

    #endregion
}
