namespace Drover.Ui.Tests.Rules;

/// <summary>
/// One opening or closing tag of a Razor file, element or component.
/// </summary>
public sealed record MarkupTag(
    string Name,
    bool IsClosing,
    bool IsSelfClosing,
    IReadOnlyList<AttributeNode> Attributes,
    int Line)
{
    #region Public Methods

    /// <summary>
    /// Names are matched exactly: <c>label</c> is the element, <c>Label</c> would be a component.
    /// </summary>
    public bool Is(params string[] names) => names.Contains(Name, StringComparer.Ordinal);

    public bool Has(string attribute) => Attributes.Any(candidate => Matches(candidate, attribute));

    public string? Value(string attribute) =>
        Attributes.FirstOrDefault(candidate => Matches(candidate, attribute))?.Value;

    public bool HasText(string attribute) => !string.IsNullOrWhiteSpace(Value(attribute));

    #endregion

    #region Private Methods

    private static bool Matches(AttributeNode candidate, string name) =>
        string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase);

    #endregion
}
