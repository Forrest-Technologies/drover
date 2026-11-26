namespace Drover.Contracts.Appearance;

/// <summary>A theme, mode, and accent that follow a person's account across devices.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AppearanceSetting(string ThemeId, string Mode, string Accent = "theme")
{
    #region Private Fields

    public const int MaxThemeIdLength = 40;

    public const int MaxAccentLength = 40;

    #endregion

    #region Properties

    public static IReadOnlyList<string> Modes { get; } = ["system", "dark", "light", "black"];

    public static IReadOnlyList<string> Accents { get; } = ["theme", "orange", "green", "blue"];

    #endregion
}
