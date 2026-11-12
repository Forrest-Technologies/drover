namespace Drover.Ui.Tests.Rules;

/// <summary>
/// What scanning one file found: the violations that stand, and the allowances that were used.
/// </summary>
public sealed record ScanResult(IReadOnlyList<Violation> Violations, IReadOnlyList<Allowance> Allowances);
