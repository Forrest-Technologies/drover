namespace Drover.Native.Theme;

/// <summary>Keeps a control that draws itself from tokens in step with the theme it is shown in.</summary>
public static class Themed
{
    /// <summary>Runs <paramref name="apply"/> when the element enters the screen and each time its theme changes.</summary>
    public static void WhenShownOrRethemed(this FrameworkElement element, Action apply)
    {
        element.Loaded += (_, _) => apply();
        element.ActualThemeChanged += (_, _) => apply();
    }
}
