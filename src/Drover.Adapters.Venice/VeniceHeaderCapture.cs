namespace Drover.Adapters.Venice;

/// <summary>A pipeline step that notes the balance headers on each response, for the client that owns it.</summary>
internal sealed class VeniceHeaderCapture : PipelinePolicy
{
    #region Private Fields

    private IReadOnlyList<ModelBalance> balances = [];

    #endregion

    #region Properties

    /// <summary>The balances on the latest response that carried any.</summary>
    public IReadOnlyList<ModelBalance> Balances => balances;

    #endregion

    #region Interface Implementations

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        ProcessNext(message, pipeline, currentIndex);
        Capture(message);
    }

    public override async ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        await ProcessNextAsync(message, pipeline, currentIndex);
        Capture(message);
    }

    #endregion

    #region Private Methods

    private void Capture(PipelineMessage message)
    {
        if (message.Response is null)
        {
            return;
        }

        var found = VeniceHeaders.ReadBalances(message.Response.Headers);

        if (found.Count > 0)
        {
            balances = found;
        }
    }

    #endregion
}
