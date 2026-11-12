namespace Drover.Domain.Policy;

/// <summary>
/// The rules as they apply inside one space: the deployment's rule, narrowed by the space's settings. Only rules
/// a space may narrow appear here.
/// </summary>
public sealed record EffectivePolicy(bool AllowPrivateAddresses, bool TaintedRunEscalation, bool AutoLoadRemoteImages);
