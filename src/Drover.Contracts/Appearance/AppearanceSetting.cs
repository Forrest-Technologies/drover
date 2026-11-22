namespace Drover.Contracts.Appearance;

/// <summary>
/// The theme a person uses on every device that follows their account: the theme's id and its mode, one of
/// <c>system</c>, <c>dark</c> and <c>light</c>. The server keeps it as given and checks only its shape; a device
/// that does not have the theme shows its default instead.
/// </summary>
public sealed record AppearanceSetting(string ThemeId, string Mode)
{
    #region Private Fields

    /// <summary>The longest theme id, the same limit as a theme file's.</summary>
    public const int MaxThemeIdLength = 40;

    #endregion

    #region Properties

    /// <summary>The modes a person may keep.</summary>
    public static IReadOnlyList<string> Modes { get; } = ["system", "dark", "light"];

    #endregion
}
