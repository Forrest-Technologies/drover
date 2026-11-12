namespace Drover.Ui.Tests.Rules;

/// <summary>
/// A stated exception to one rule, written as a comment in the file it applies to.
/// Alone on its line it covers the line after it; after code on the same line it covers that line only.
/// </summary>
public sealed record Allowance(string Rule, string File, int Line, int LastLine, string Reason)
{
    #region Public Methods

    public bool Covers(Violation violation) =>
        violation.Rule == Rule && violation.Line >= Line && violation.Line <= LastLine;

    public override string ToString() => $"{File}:{Line} allows {Rule} because {Reason}";

    #endregion
}
