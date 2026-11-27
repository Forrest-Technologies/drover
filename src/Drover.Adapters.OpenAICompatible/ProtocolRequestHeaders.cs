namespace Drover.Adapters.OpenAICompatible;

/// <summary>Puts the headers a provider class named on every request of one call.</summary>
internal sealed class ProtocolRequestHeaders(IReadOnlyDictionary<string, string> headers) : PipelinePolicy
{
    #region Interface Implementations

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Add(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Add(message);
        return ProcessNextAsync(message, pipeline, currentIndex);
    }

    #endregion

    #region Private Methods

    private void Add(PipelineMessage message)
    {
        foreach (var (name, value) in headers)
        {
            message.Request.Headers.Set(name, value);
        }
    }

    #endregion
}
