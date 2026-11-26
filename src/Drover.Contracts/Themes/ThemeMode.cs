namespace Drover.Contracts.Themes;

/// <summary>Which mode of a theme a person chose. System follows the device, to dark or to light.</summary>
public enum ThemeMode
{
    System,
    Dark,
    Light,

    /// <summary>The dark mode on a black first surface, for a screen that turns its pixels off.</summary>
    Black,
}
