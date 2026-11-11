namespace Drover.Contracts.Permissions;

/// <summary>
/// What a bot may do with a tool. The order is the rule: <c>Deny</c> &lt; <c>Ask</c> &lt; <c>Allow</c>, and the
/// effective grant is the minimum along the path to the root. No grant at all is not a value here: it is a missing
/// row, and it means the tool is invisible.
/// </summary>
public enum GrantEffect
{
    Deny = 0,
    Ask = 1,
    Allow = 2,
}
