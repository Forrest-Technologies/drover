namespace Drover.Domain.Policy;

/// <summary>Who decides how long the audit log is kept. Ordered from strictest to loosest.</summary>
public enum AuditRetentionMode
{
    MinimumEnforced,
    OperatorChoice,
}
