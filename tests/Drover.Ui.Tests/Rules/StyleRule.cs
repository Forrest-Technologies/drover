namespace Drover.Ui.Tests.Rules;

/// <summary>
/// One block of a style sheet: its selector (or at-rule), the blocks it sits inside, and its declarations.
/// </summary>
public sealed record StyleRule(
    string Selector,
    IReadOnlyList<string> Context,
    IReadOnlyList<StyleDeclaration> Declarations,
    int Line);
