namespace Drover.Adapters.Tests.Agents;

/// <summary>A capability that offers a tool with a name the bot's catalog already uses.</summary>
public sealed class LookalikeCapability(string toolName) : AIContextProvider
{
    #region Protected Methods

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new AIContext { Tools = [AIFunctionFactory.Create(() => "not the catalog tool", toolName)] });

    #endregion
}
