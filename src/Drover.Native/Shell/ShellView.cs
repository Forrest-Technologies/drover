namespace Drover.Native.Shell;

/// <summary>
/// The root of the app: the screen for the current address, with the toasts over it. It listens to the navigator
/// and shows the screen each address names, keeps the frame's own back stack in step, applies the theme choice,
/// and names the window after the screen. The appearance choices are its own dialog, because a choice makes the
/// screen again and the choices stay open over it.
/// </summary>
public sealed class ShellView : Grid
{
    #region Private Fields

    private readonly Frame frame = new() { IsTabStop = false };

    private readonly IServiceProvider services;

    private readonly Navigator navigator;

    private readonly ThemeModel theme;

    private readonly Window window;

    private readonly AppearanceViewModel appearanceModel;

    private Dialog? appearance;

    #endregion

    #region Constructors

    public ShellView(IServiceProvider services, Window window)
    {
        (this.services, this.window) = (services, window);
        navigator = services.GetRequiredService<Navigator>();
        theme = services.GetRequiredService<ThemeModel>();
        appearanceModel = ActivatorUtilities.CreateInstance<AppearanceViewModel>(services);

        Children.Add(frame);
        Children.Add(new ToastHost { Model = ActivatorUtilities.CreateInstance<ToastHostViewModel>(services) });
#if ANDROID || __IOS__
        // Keep the window background under the system bars and the controls inside their visible bounds.
        SafeArea.Attach(this);
#endif

        theme.Load();
        theme.PropertyChanged += (_, _) => ThemeChanged();
        navigator.Navigated += Show;
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => DispatcherQueue.TryEnqueue(PlatformShell.Shown);
#if DEBUG
        Loaded += (_, _) => AppearanceRehearsal.StartIfAsked(window, theme);
#endif
        _ = ApplyTheme();
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
    private void Go(Type screen, RouteMatch<Type> match, NavigationKind kind, bool rebuild = false)
    {
        // A screen that shows a tab named in its address stays as it is when only that changes.
        if (!rebuild && frame.Content is Screen open && open.GetType() == screen && open.Reopen(match))
        {
            PlatformShell.SetTitle(window, open.Title);
            return;
        }

        if (rebuild)
        {
            // A frame does not navigate to the page and parameter it already shows, so it passes through a blank
            // page. Neither stop is kept in the back stack.
            var instantly = new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo();
            frame.Navigate(typeof(Page), null, instantly);
            frame.Navigate(screen, match, instantly);
            frame.BackStack.RemoveAt(frame.BackStack.Count - 1);
            frame.BackStack.RemoveAt(frame.BackStack.Count - 1);
        }
        else if (kind is NavigationKind.Travel && frame.CanGoBack && AddressOf(frame.BackStack[^1]) == match.Address)
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

        // Whatever was made is made from the values now in place.
        appearanceModel.ScreenMade();

        if (frame.Content is Screen shown)
        {
            PlatformShell.SetTitle(window, shown.Title);

            // The new screen takes the focus, so it is announced and the keyboard starts at its top. A screen made
            // again under the appearance choices leaves the focus with them.
            if (!(rebuild && theme.AppearanceOpen))
            {
                shown.Focus(FocusState.Programmatic);
            }
        }
    }

    private static string? AddressOf(Microsoft.UI.Xaml.Navigation.PageStackEntry entry) =>
        (entry.Parameter as RouteMatch<Type>)?.Address;

    // The mode is the window's requested theme: dark until a person picks light, or asks to follow the device. Colors
    // follow it at once. A window has no black mode, so black is the dark mode with the theme's black colors in it.
    // Another theme, another accent or the black mode puts new values in the resources, and the open screen is made
    // again from them, so every group changes without a restart.
    private void ThemeChanged()
    {
        var remade = ApplyTheme();
        ShowAppearance(remade);
    }

    private bool ApplyTheme()
    {
        RequestedTheme = theme.Choice.Mode switch
        {
            ThemeMode.Light => ElementTheme.Light,
            ThemeMode.System => ElementTheme.Default,
            _ => ElementTheme.Dark,
        };

        if (ReferenceEquals(theme.Theme, ThemeTokens.Current) && theme.IsBlack == ThemeTokens.Black)
        {
            return false;
        }

        ThemeTokens.Apply(theme.Theme, theme.IsBlack);
        Paint();

        // A screen that holds what a person typed keeps the shapes it was made with until it is left, because
        // making it again would drop that work. Its colors have changed already.
        if (frame.Content is Screen { Route: { } route } open && appearanceModel.MakesScreenAgain(HoldsSomethingTyped(open)))
        {
            Go(open.GetType(), route, NavigationKind.Replace, rebuild: true);
        }

        return true;
    }

    // What the shell can see of unsaved work on any screen: text in a field a person can write in, or a message
    // with words or files not sent yet. A field showing a saved value counts too, which only makes the screen wait.
    private static bool HoldsSomethingTyped(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var typed = VisualTreeHelper.GetChild(parent, index) switch
            {
                TextBox { IsReadOnly: false, Text.Length: > 0 } => true,
                PasswordBox { Password.Length: > 0 } => true,
                Composer { Model.HasSomething: true } => true,
                var child => HoldsSomethingTyped(child),
            };

            if (typed)
            {
                return true;
            }
        }

        return false;
    }

    // The dialog is in the shell only while it is open. Sizes, shapes and type are read when a control is made, so
    // after a choice that put new values in the resources the dialog is made again, and the option that had the
    // focus has it again. The new one opens over the old one with no fade, and then the old one goes, so the screen
    // behind is never within reach in between.
    private void ShowAppearance(bool remade)
    {
        if (!theme.AppearanceOpen)
        {
            CloseAppearance();
            return;
        }

        if (appearance is not null && !remade)
        {
            return;
        }

        var before = appearance;
        var dialog = new Dialog
        {
            Title = "Appearance",
            Body = new AppearanceChoices(appearanceModel) { FocusHeld = (before?.Body as AppearanceChoices)?.FocusHeld },
            OpensAtOnce = before is not null,
        };
        dialog.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) => AppearanceClosedByPerson(dialog));
        appearance = dialog;
        Children.Add(dialog);
        dialog.Open = true;
        Remove(before);
    }

    // Escape, Back or the Close button.
    private void AppearanceClosedByPerson(Dialog dialog)
    {
        if (ReferenceEquals(dialog, appearance) && !dialog.Open)
        {
            theme.AppearanceOpen = false;
        }
    }

    private void CloseAppearance()
    {
        if (appearance is not { } dialog)
        {
            return;
        }

        appearance = null;
        Remove(dialog);

        // After the dialog has let go of the focus, which it does as it leaves.
        DispatcherQueue.TryEnqueue(() => FocusAfterAppearance(dialog.ClosedWith));
    }

    private void Remove(Dialog? dialog)
    {
        if (dialog is not null)
        {
            dialog.Open = false;
            dialog.Body = null;
            Children.Remove(dialog);
        }
    }

    // The account menu that opened the choices may be gone with its screen. The focus goes to the button of the
    // account menu now showing, and to the top of the screen where there is none. The ring shows on the button only
    // when the keyboard closed the choices.
    private void FocusAfterAppearance(FocusState closedWith)
    {
        if (frame.Content is not Screen shown || theme.AppearanceOpen)
        {
            return;
        }

        if (!AccountMenusUnder(shown).Any(menu => menu.ActualWidth > 0 && menu.FocusTrigger(closedWith)))
        {
            shown.Focus(FocusState.Programmatic);
        }
    }

    private static IEnumerable<AccountMenu> AccountMenusUnder(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is AccountMenu menu)
            {
                yield return menu;
            }
            else
            {
                foreach (var deeper in AccountMenusUnder(child))
                {
                    yield return deeper;
                }
            }
        }
    }

    private void Paint()
    {
        Background = Tokens.Brush(this, "DvBg0");

        // The window clears to this before it draws anything, so its first frame, and the edge a resize uncovers,
        // are the first surface and not the platform's white.
        Uno.UI.Xaml.WindowHelper.SetBackground(window, Background);
#if ANDROID
        if (Uno.UI.ContextHelper.Current is MainActivity activity)
        {
            activity.UpdateSystemBars(this);
        }
#endif
    }

    #endregion
}
