namespace Drover.Ui.Tests.Rules;

/// <summary>
/// One <c>property: value</c> pair. The value is kept as written, so a position in it maps back to a line.
/// </summary>
public sealed record StyleDeclaration(string Property, string Value, int Line, int ValueLine)
{
    #region Public Methods

    public int LineOf(int valueIndex) => ValueLine + Value.AsSpan(0, valueIndex).Count('\n');

    #endregion
}
