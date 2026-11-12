namespace Drover.Adapters.OpenAICompatible;

/// <summary>One capture per operation, never shared across calls on a client.</summary>
internal sealed class ProtocolHeaderCapture : PipelinePolicy
{
    #region Properties

    public IReadOnlyDictionary<string, string> Headers { get; private set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Interface Implementations

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        ProcessNext(message, pipeline, currentIndex);
        Capture(message);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        await ProcessNextAsync(message, pipeline, currentIndex);
        Capture(message);
    }

    #endregion

    #region Private Methods

    private void Capture(PipelineMessage message) => Headers = message.Response?.Headers
        .Where(header => header.Key.StartsWith("x-ratelimit-", StringComparison.OrdinalIgnoreCase)
            || header.Key.StartsWith("x-venice-balance-", StringComparison.OrdinalIgnoreCase)
            || header.Key.Equals("Retry-After", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(header => header.Key, header => header.Value, StringComparer.OrdinalIgnoreCase)
        ?? [];

    #endregion
}
