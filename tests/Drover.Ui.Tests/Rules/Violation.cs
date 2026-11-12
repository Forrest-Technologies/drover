namespace Drover.Ui.Tests.Rules;

/// <summary>
/// One place where an interface file breaks a rule from DESIGN.md.
/// </summary>
public sealed record Violation(string Rule, string File, int Line, string Message)
{
    #region Public Methods

    public override string ToString() => $"{File}:{Line} {Rule} {Message}";

    #endregion
}
