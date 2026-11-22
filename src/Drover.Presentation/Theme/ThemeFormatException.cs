namespace Drover.Ui.Theme;

/// <summary>
/// A theme file does not follow the closed format. It lists every problem found, each with where it is.
/// </summary>
public sealed class ThemeFormatException(IReadOnlyList<string> problems)
    : Exception($"The theme file is not valid: {string.Join("; ", problems)}")
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
