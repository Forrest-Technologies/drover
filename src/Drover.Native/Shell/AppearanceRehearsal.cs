#if DEBUG
namespace Drover.Native.Shell;

/// <summary>
/// For taking a picture of the appearance choices with no pointer to press them: a development build started with
/// <c>DROVER_DEV_APPEARANCE</c> set opens the choices over the first screen. The words in it, separated by commas,
/// are buttons to press in turn, each the start of a button's accessible name or its words, so <c>Close</c> closes
/// the dialog. Each is given the keyboard's focus and pressed through its automation peer, as a person's key press
/// would. <c>DROVER_DEV_WIDTH</c> makes the window that wide first. Not in a product build.
/// </summary>
internal static class AppearanceRehearsal
{
    #region Private Fields

    private static readonly TimeSpan pause = TimeSpan.FromSeconds(2);

    #endregion

    #region Public Methods

    public static void StartIfAsked(Window window, ThemeModel theme)
    {
        if (Environment.GetEnvironmentVariable("DROVER_DEV_APPEARANCE") is { Length: > 0 } asked && !theme.AppearanceOpen)
        {
            _ = Run(window, theme, asked);
        }
    }

    #endregion

    #region Private Methods

    private static async Task Run(Window window, ThemeModel theme, string asked)
    {
        var size = window.AppWindow.Size;
        if (int.TryParse(Environment.GetEnvironmentVariable("DROVER_DEV_WIDTH"), out var width))
        {
            window.AppWindow.Resize(new(width, size.Height));
        }

        await Task.Delay(pause);
        theme.AppearanceOpen = true;

        foreach (var name in asked.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await Task.Delay(pause);
            Press(window, name);
        }
    }

    private static void Press(Window window, string name)
    {
        if (window.Content?.XamlRoot is not { } root)
        {
            return;
        }

        var option = VisualTreeHelper.GetOpenPopupsForXamlRoot(root)
            .SelectMany(popup => ButtonsUnder(popup.Child))
            .FirstOrDefault(button => Named(button).StartsWith(name, StringComparison.OrdinalIgnoreCase));

        if (option is not null
            && FrameworkElementAutomationPeer.CreatePeerForElement(option) is IInvokeProvider invoker)
        {
            option.Focus(FocusState.Keyboard);
            invoker.Invoke();
        }
    }

    private static string Named(Microsoft.UI.Xaml.Controls.Button button) =>
        AutomationProperties.GetName(button) is { Length: > 0 } name ? name : button.Content as string ?? string.Empty;

    private static IEnumerable<Microsoft.UI.Xaml.Controls.Button> ButtonsUnder(DependencyObject? parent)
    {
        if (parent is null)
        {
            yield break;
        }

        if (parent is Microsoft.UI.Xaml.Controls.Button button)
        {
            yield return button;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            foreach (var deeper in ButtonsUnder(VisualTreeHelper.GetChild(parent, index)))
            {
                yield return deeper;
            }
        }
    }

    #endregion
}
#endif
