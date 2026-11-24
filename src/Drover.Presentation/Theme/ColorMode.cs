namespace Drover.Ui.Theme;

/// <summary>
/// One of the modes every theme has. A theme file holds the dark and the light one; black is the dark one worked out
/// for a screen that turns its pixels off. A person's choice of <see cref="ThemeMode.System"/> becomes dark or light.
/// </summary>
public enum ColorMode
{
    Dark,
    Light,
    Black,
}
