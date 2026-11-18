namespace Drover.Native.Shell;

/// <summary>
/// The root of the app: the screen for the current address, with the toasts over it. It listens to the navigator
/// and shows the screen each address names, keeps the frame's own back stack in step, applies the theme choice,
/// and names the window after the screen.
/// </summary>
public sealed class ShellView : Grid
{
    #region Private Fields

    private readonly Frame frame = new() { IsTabStop = false };

    private readonly IServiceProvider services;

    private readonly Navigator navigator;

    private readonly ThemeModel theme;

    private readonly Window window;

    #endregion

    #region Constructors

    public ShellView(IServiceProvider services, Window window)
    {
        (this.services, this.window) = (services, window);
        navigator = services.GetRequiredService<Navigator>();
        theme = services.GetRequiredService<ThemeModel>();

        Children.Add(frame);
        Children.Add(new ToastHost { Model = ActivatorUtilities.CreateInstance<ToastHostViewModel>(services) });

        theme.Load();
        theme.PropertyChanged += (_, _) => ApplyTheme();
        navigator.Navigated += Show;
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => DispatcherQueue.TryEnqueue(PlatformShell.Shown);
        ApplyTheme();
        Paint();
    }

    #endregion

    #region Public Methods

    /// <summary>Shows the screen for the address the app was opened at.</summary>
    public void Open() => Show(navigator.Current, NavigationKind.Replace);

    #endregion

    #region Private Methods

    private void Show(string address, NavigationKind kind)
    {
        if (PlatformShell.ScreenBeforeServer(services) is { } first)
        {
            Go(first, new RouteMatch<Type>(first, address, EmptyValues, EmptyValues, null), NavigationKind.Replace);
            return;
        }

        var match = Screens.Table.Match(address)
            ?? new RouteMatch<Type>(typeof(NotFoundPage), address, EmptyValues, EmptyValues, null);
        Go(match.Screen, match, kind);
    }

    private static IReadOnlyDictionary<string, string> EmptyValues => new Dictionary<string, string>();

    // The frame keeps its own back stack. A step back or forward through history reuses it when the entry there is
    // the address asked for, and otherwise the screen is simply shown in place.
    private void Go(Type screen, RouteMatch<Type> match, NavigationKind kind)
    {
        // A screen that shows a tab named in its address stays as it is when only that changes.
        if (frame.Content is Screen open && open.GetType() == screen && open.Reopen(match))
        {
            PlatformShell.SetTitle(window, open.Title);
            return;
        }

        if (kind is NavigationKind.Travel && frame.CanGoBack && AddressOf(frame.BackStack[^1]) == match.Address)
        {
            frame.GoBack();
        }
        else if (kind is NavigationKind.Travel && frame.CanGoForward && AddressOf(frame.ForwardStack[^1]) == match.Address)
        {
            frame.GoForward();
        }
        else
        {
            frame.Navigate(screen, match, new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
            if (kind is not NavigationKind.Push && frame.BackStack.Count > 0)
            {
                frame.BackStack.RemoveAt(frame.BackStack.Count - 1);
            }
        }

        if (frame.Content is Screen shown)
        {
            PlatformShell.SetTitle(window, shown.Title);

            // The new screen takes the focus, so it is announced and the keyboard starts at its top.
            shown.Focus(FocusState.Programmatic);
        }
    }

    private static string? AddressOf(Microsoft.UI.Xaml.Navigation.PageStackEntry entry) => (entry.Parameter as RouteMatch<Type>)?.Address;

    // Dark until a person picks light, or asks to follow the device.
    private void ApplyTheme() => RequestedTheme = theme.Choice switch
    {
        ThemeChoice.Light => ElementTheme.Light,
        ThemeChoice.System => ElementTheme.Default,
        _ => ElementTheme.Dark,
    };

    private void Paint() => Background = Tokens.Brush(this, "DvBg0");

    #endregion
}
