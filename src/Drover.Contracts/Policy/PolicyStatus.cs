namespace Drover.Contracts.Policy;

/// <summary>The active deployment preset and every rule with its setting and where it is enforced.</summary>
public sealed record PolicyStatus(string Preset, IReadOnlyList<PolicyRuleStatus> Rules);

/// <summary>
/// One rule. <c>Setting</c> is "true", "false", or the name of the choice. <c>EnforcedBy</c> says where the rule is
/// enforced; when <c>IsEnforced</c> is false it names the work that will enforce it.
/// </summary>
public sealed record PolicyRuleStatus(string Rule, string Setting, bool IsEnforced, string EnforcedBy);
