namespace Drover.Agents.Governance;

/// <summary>
/// Wraps a capability's context provider and refuses a tool whose name another tool in the run already has. The
/// model calls tools by name, so two tools with one name would let a call meant for one reach the other.
/// </summary>
public sealed class CapabilityGuard(AIContextProvider inner, IReadOnlyList<string> catalogNames) : AIContextProvider
{
    #region Properties

    public override IReadOnlyList<string> StateKeys => inner.StateKeys;

    #endregion

    #region Public Methods

    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        base.GetService(serviceType, serviceKey) ?? inner.GetService(serviceType, serviceKey);

    #endregion

    #region Protected Methods

    protected override async ValueTask<AIContext> InvokingCoreAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var merged = await inner.InvokingAsync(context, cancellationToken);

        // The merged context may or may not repeat the catalog tools; the catalog's own functions are told apart by
        // identity, so only the tools a capability added are compared with the catalog names.
        var added = (merged.Tools ?? []).Where(tool => tool.GetService<GovernedToolFunction>() is null).Select(tool => tool.Name);
        GovernedAgentBuilder.RejectDuplicateNames([.. catalogNames, .. added]);
        return merged;
    }

    protected override ValueTask InvokedCoreAsync(InvokedContext context, CancellationToken cancellationToken = default) =>
        inner.InvokedAsync(context, cancellationToken);

    #endregion
}
