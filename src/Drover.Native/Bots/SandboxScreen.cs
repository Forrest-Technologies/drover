namespace Drover.Native.Bots;

/// <summary>
/// A bot's sandbox screen, live, wherever a person looks at it: on the sandbox screen and beside the conversation.
/// A person watches it and can send mouse and keyboard input. The picture is asked of the server through the app's
/// own client, as everything else is, and the server decides who may see or drive it; this control only draws what
/// came and sends what the person did. What it says and does is in <see cref="SandboxScreenViewModel"/>.
/// </summary>
public sealed class SandboxScreen : StackPanel
{
    #region Private Fields

    private readonly SandboxScreenViewModel model =
        ActivatorUtilities.CreateInstance<SandboxScreenViewModel>(((App)Application.Current).Services);

    private readonly FlowPanel controlBar = Row("DvSpace2");

    private readonly Switch control = new() { Label = SandboxScreenViewModel.ControlLabel, HorizontalAlignment = HorizontalAlignment.Left };

    private readonly Button openTerminal = new() { Content = SandboxScreenViewModel.OpenTerminalLabel };

    private readonly SegmentedControl clickMode = new()
    {
        Label = SandboxScreenViewModel.ClickModeLabel,
        Options = SandboxScreenViewModel.ClickModes,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock controlWords = Hint(live: true);

    private readonly StatusNote working = new();

    private readonly TextBlock screenNote = Hint(live: false);

    private readonly Button wake = new() { HorizontalAlignment = HorizontalAlignment.Left };

    private readonly SandboxGroup zoom = new(SandboxScreenViewModel.ZoomLabel, "DvSpace1") { HorizontalAlignment = HorizontalAlignment.Right };

    private readonly Button zoomOut = new() { Content = "Zoom out" };

    private readonly Button zoomIn = new() { Content = "Zoom in" };

    private readonly Button fit = new() { Content = "Fit screen" };

    private readonly TextBlock zoomLevel = Hint(live: true);

    private readonly SandboxPicture picture;

    private readonly TextBlock progress = Hint(live: true);

    private readonly StatusNote problem = new() { Kind = NoteKind.Danger };

    private readonly FlowPanel options = Row("DvSpace2");

    private readonly Switch passwordMode = new() { Label = SandboxScreenViewModel.PasswordModeLabel, HorizontalAlignment = HorizontalAlignment.Left };

    private readonly Switch enterAfter = new() { Label = SandboxScreenViewModel.EnterAfterLabel, HorizontalAlignment = HorizontalAlignment.Left };

    private readonly Field typed = new();

    private readonly Button type = new() { Kind = ButtonKind.Primary };

    private readonly SandboxForm typeForm;

    private readonly TextBlock typeHint = Hint(live: false);

    private readonly SandboxGroup keys = new(SandboxScreenViewModel.KeysLabel, "DvSpace2");

    private readonly Menu combinations = new() { Label = SandboxScreenViewModel.CombinationsLabel, VerticalAlignment = VerticalAlignment.Center };

    private readonly SandboxGroup scroll = new(SandboxScreenViewModel.ScrollLabel, "DvSpace2");

    private readonly List<Control> sendButtons = [];

    private Control? focusedWhileSending;

    private bool wasSending;

    private bool typing;

    private bool leaveWhenReleased;

    #endregion

    #region Constructors

    public SandboxScreen()
    {
        picture = new(model);
        typeForm = new(typed, type);
        Spacing = Tokens.Number("DvSpace3");
        Padding = new(0, Tokens.Number("DvSpace3"), 0, 0);

        BuildControlBar();
        BuildZoom();
        BuildTyping();
        BuildKeys();

        foreach (var part in new UIElement[]
        {
            controlBar, controlWords, working, screenNote, wake, zoom, picture, progress, problem, options, typeForm, typeHint, keys, scroll,
        })
        {
            Children.Add(part);
        }

        wake.Click += (_, _) => model.Wake();
        model.PropertyChanged += (_, _) => Refresh();
        model.SandboxChanged += TellSandboxChanged;
        model.KeepTyping += typed.TakeFocus;
        picture.Released += LeaveTypingIfDue;
        Loaded += (_, _) => Shown();
        Unloaded += (_, _) => Hidden();
        SizeChanged += (_, _) => FitToWidth();
        Refresh();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty BotIdProperty =
        Dp.Register<SandboxScreen, Guid>(nameof(BotId), Guid.Empty, screen => screen.model.BotId = screen.BotId);

    public static readonly DependencyProperty BotNameProperty =
        Dp.Register<SandboxScreen, string>(nameof(BotName), string.Empty, screen => screen.model.BotName = screen.BotName);

    public static readonly DependencyProperty StatusProperty =
        Dp.Register<SandboxScreen, BotSandboxStatus?>(nameof(Status), null, screen => screen.model.Status = screen.Status);

    public static readonly DependencyProperty HeldProperty =
        Dp.Register<SandboxScreen, bool>(nameof(Held), false, screen => screen.model.Held = screen.Held);

    public Guid BotId
    {
        get => (Guid)GetValue(BotIdProperty);
        set => SetValue(BotIdProperty, value);
    }

    public string BotName
    {
        get => (string)GetValue(BotNameProperty);
        set => SetValue(BotNameProperty, value);
    }

    /// <summary>The sandbox as it is now. The picture is read only while it runs. Nothing shows until it is given.</summary>
    public BotSandboxStatus? Status
    {
        get => (BotSandboxStatus?)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>
    /// The person holds the screen because the bot asked them to take it over. Take control turns on when the hold
    /// begins and off when it ends, so the person and the bot never both drive the screen.
    /// </summary>
    public bool Held
    {
        get => (bool)GetValue(HeldProperty);
        set => SetValue(HeldProperty, value);
    }

    /// <summary>
    /// Raised after the person started or resumed the sandbox here, or tried to, so whatever shows the screen reads
    /// the state again at once instead of on its timer.
    /// </summary>
    public event EventHandler? SandboxChanged;

    private static Window? AppWindow => ((App)Application.Current).MainWindow;

    #endregion

    #region Building

    private void BuildControlBar()
    {
        control.RegisterPropertyChangedCallback(Switch.ValueProperty, (_, _) =>
        {
            if (control.Value != model.Control)
            {
                model.SetControl(control.Value);
            }
        });
        clickMode.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) => model.ClickMode = clickMode.Value ?? model.ClickMode);
        openTerminal.Click += async (_, _) => await model.OpenTerminal();
        sendButtons.Add(openTerminal);
        controlBar.Children.Add(control);
        controlBar.Children.Add(openTerminal);
        controlBar.Children.Add(clickMode);
    }

    private void BuildZoom()
    {
        // The level keeps the width of its widest value, so the buttons beside it do not move as it changes.
        var widest = Hint(live: false);
        widest.Text = new ScreenZoom(ScreenZoom.MaxScale, 0, 0).Percent;
        widest.Opacity = 0;
        AutomationProperties.SetAccessibilityView(widest, AccessibilityView.Raw);
        zoomLevel.HorizontalAlignment = HorizontalAlignment.Center;
        var level = new Grid { MinHeight = Targets.Minimum };
        widest.VerticalAlignment = zoomLevel.VerticalAlignment = VerticalAlignment.Center;
        level.Children.Add(widest);
        level.Children.Add(zoomLevel);

        zoomOut.Click += (_, _) => model.ZoomOut();
        zoomIn.Click += (_, _) => model.ZoomIn();
        fit.Click += (_, _) => model.FitScreen();
        zoom.Add(zoomOut, level, zoomIn, fit);
    }

    private void BuildTyping()
    {
        passwordMode.RegisterPropertyChangedCallback(Switch.ValueProperty, (_, _) =>
        {
            if (passwordMode.Value != model.PasswordMode)
            {
                model.SetPasswordMode(passwordMode.Value);
            }
        });
        enterAfter.RegisterPropertyChangedCallback(Switch.ValueProperty, (_, _) => model.EnterAfter = enterAfter.Value);
        options.Children.Add(passwordMode);
        options.Children.Add(enterAfter);

        typed.RegisterPropertyChangedCallback(Field.ValueProperty, (_, _) => model.Typed = typed.Value);
        typed.Submitted += async (_, _) => await model.TypeText();
        type.Click += async (_, _) => await model.TypeText();

        // Send does not take the focus from the field, so the on-screen keyboard stays up.
        type.AllowFocusOnInteraction = false;
        typeForm.GotFocus += (_, _) => EnterTyping();
        typeForm.LostFocus += (_, _) => DispatcherQueue.TryEnqueue(LeaveTyping);
    }

    private void BuildKeys()
    {
        foreach (var key in SandboxScreenViewModel.Keys)
        {
            var press = new Button { Content = key.Label };
            AutomationProperties.SetName(press, key.Name);
            press.Click += async (_, _) => await model.Press(key.Keys);
            sendButtons.Add(press);
            keys.Add(press);
        }

        foreach (var combination in SandboxScreenViewModel.Combinations)
        {
            var item = new MenuItem { Content = combination.Label };
            item.Selected += async (_, _) => await model.Press(combination.Keys);
            sendButtons.Add(item);
            combinations.Items.Add(item);
        }

        keys.Add(combinations);

        foreach (var up in new[] { true, false })
        {
            var turn = new Button { Content = up ? "Scroll up" : "Scroll down" };
            turn.Click += async (_, _) => await model.Scroll(up);
            sendButtons.Add(turn);
            scroll.Add(turn);
        }
    }

    private static FlowPanel Row(string gap) => new() { Gap = Tokens.Number(gap) };

    private static TextBlock Hint(bool live)
    {
        var hint = Styles.Text(null, "DvTextSecondary");
        hint.FontSize = Tokens.Number("DvText13");
        if (live)
        {
            AutomationProperties.SetLiveSetting(hint, AutomationLiveSetting.Polite);
        }

        return hint;
    }

    #endregion

    #region Showing

    private void Refresh()
    {
        var ready = model.DesktopReady;
        var driving = ready && model.Control;
        KeepFocusWhileSending();

        Show(controlBar, ready);
        control.Value = model.Control;
        openTerminal.IsEnabled = !model.Sending;
        Show(clickMode, model.Control);
        clickMode.Value = model.ClickMode;
        Say(controlWords, ready ? model.ControlWords : null);
        working.Text = model.WorkingNote;
        Show(working, model.WorkingNote is not null);
        Say(screenNote, model.ScreenNote);
        wake.Content = model.WakeLabel;
        Show(wake, model.WakeLabel is not null);

        Show(zoom, ready);
        zoomOut.IsEnabled = fit.IsEnabled = !model.Zoom.IsFit;
        zoomIn.IsEnabled = !model.Zoom.IsMax;
        zoomLevel.Text = model.Zoom.Percent;
        Show(picture, ready);
        picture.Show();
        Say(progress, model.ProgressNote);
        problem.Text = model.ScreenProblem;

        Show(options, driving);
        passwordMode.Value = model.PasswordMode;
        passwordMode.IsEnabled = !model.Sending;
        Show(enterAfter, !model.PasswordMode);
        enterAfter.Value = model.EnterAfter;
        Show(typeForm, driving);
        ShowTyping();
        Say(typeHint, driving ? model.TypeHint : null);
        Show(keys, driving);
        Show(scroll, driving);

        foreach (var button in sendButtons)
        {
            button.IsEnabled = !model.Sending;
        }

        RestoreFocusAfterSending();
    }

    private void ShowTyping()
    {
        var kind = model.PasswordMode ? "password" : "text";
        typed.Label = model.TypeLabel;
        if (typed.Type != kind)
        {
            typed.Type = kind;
            TuneInput(typed, FieldLimits.TypedText);
        }

        if (typed.Value != model.Typed)
        {
            typed.Value = model.Typed;
        }

        type.Content = model.TypeAction;
        type.IsEnabled = model.CanType;
    }

    // A control that is switched off while it has the focus loses it. What a person sends switches the buttons off
    // for a moment, so the focus is put back where it was once they are on again.
    private void KeepFocusWhileSending()
    {
        if (model.Sending && !wasSending && XamlRoot is not null
            && FocusManager.GetFocusedElement(XamlRoot) is Control focused && sendButtons.Contains(focused))
        {
            focusedWhileSending = focused;
        }
    }

    private void RestoreFocusAfterSending()
    {
        if (!model.Sending && wasSending && focusedWhileSending is { } focused)
        {
            focusedWhileSending = null;
            if (focused.IsLoaded && focused.Visibility is Visibility.Visible)
            {
                focused.Focus(FocusState.Keyboard);
            }
        }

        wasSending = model.Sending;
    }

    private static void Show(UIElement part, bool shown) => part.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

    private static void Say(TextBlock line, string? words)
    {
        line.Text = words ?? string.Empty;
        Show(line, words is not null);
    }

    #endregion

    #region Layout

    private void FitToWidth()
    {
        // A narrow window or a finger gets tighter zoom buttons, so the four stay on one row.
        var narrow = Targets.IsCoarse || (XamlRoot?.Size.Width ?? ActualWidth) < (double)Styles.Dictionary["DvBreakPhone"];
        var side = Tokens.Number(narrow ? "DvSpace2" : "DvSpace4");
        zoomOut.Padding = zoomIn.Padding = fit.Padding = new(side, 0, side, 0);
        if (Targets.IsCoarse)
        {
            foreach (var button in sendButtons.OfType<Button>().Append(zoomOut).Append(zoomIn).Append(fit).Append(wake).Append(type))
            {
                button.MinWidth = Tokens.Number("DvTargetTouch");
            }
        }
    }

    #endregion

    #region Watching

    private void Shown()
    {
        if (AppWindow is { } window)
        {
            window.VisibilityChanged -= WindowShownOrHidden;
            window.VisibilityChanged += WindowShownOrHidden;
        }

        var pane = Windows.UI.ViewManagement.InputPane.GetForCurrentView();
        pane.Showing -= KeyboardChanged;
        pane.Hiding -= KeyboardChanged;
        pane.Showing += KeyboardChanged;
        pane.Hiding += KeyboardChanged;

        TuneInput(typed, FieldLimits.TypedText);
        FitToWidth();
        model.Start();
    }

    private async void Hidden()
    {
        if (AppWindow is { } window)
        {
            window.VisibilityChanged -= WindowShownOrHidden;
        }

        var pane = Windows.UI.ViewManagement.InputPane.GetForCurrentView();
        pane.Showing -= KeyboardChanged;
        pane.Hiding -= KeyboardChanged;
        await model.Stop();
    }

    // While nobody looks at the app the picture stops refreshing.
    private void WindowShownOrHidden(object sender, WindowVisibilityChangedEventArgs visibility) => model.VisibilityChanged(visibility.Visible);

    private Task TellSandboxChanged()
    {
        SandboxChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    #endregion

    #region Typing With The On-Screen Keyboard

    /// <summary>
    /// What the text field of a sandbox form needs beyond a plain field: a longest length, no correction of what is
    /// typed, and on a phone the size at which the system does not zoom to it.
    /// </summary>
    internal static void TuneInput(Field field, int longest, bool machine = false)
    {
        field.MaxLength = longest;
        if (Inner(field) is not { } input)
        {
            return;
        }

        if (input is TextBox box)
        {
            box.IsSpellCheckEnabled = false;
        }

        if (machine)
        {
            input.FontFamily = Tokens.FontMono;
        }

        if (Targets.IsCoarse)
        {
            input.FontSize = Tokens.Number("DvText16");
        }
    }

    private static Control? Inner(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TextBox or PasswordBox)
            {
                return (Control)child;
            }

            if (Inner(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void KeyboardChanged(Windows.UI.ViewManagement.InputPane pane, Windows.UI.ViewManagement.InputPaneVisibilityEventArgs args) =>
        DispatcherQueue.TryEnqueue(FitAboveKeyboard);

    private void EnterTyping()
    {
        if (!Targets.IsCoarse || XamlRoot is null || FocusManager.GetFocusedElement(XamlRoot) is not (TextBox or PasswordBox))
        {
            return;
        }

        (typing, leaveWhenReleased) = (true, false);
        DispatcherQueue.TryEnqueue(FitAboveKeyboard);
    }

    // Focus leaves at the start of a tap. The picture keeps its size until the tap is over, so the tap lands where
    // the finger was.
    private void LeaveTyping()
    {
        if (!typing || (XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && IsInForm(focused)))
        {
            return;
        }

        if (picture.IsPressed)
        {
            leaveWhenReleased = true;
            return;
        }

        typing = false;
        picture.MaxWidth = double.PositiveInfinity;
    }

    private void LeaveTypingIfDue()
    {
        if (leaveWhenReleased)
        {
            leaveWhenReleased = false;
            DispatcherQueue.TryEnqueue(LeaveTyping);
        }
    }

    private bool IsInForm(DependencyObject element)
    {
        for (var at = element; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (ReferenceEquals(at, typeForm))
            {
                return true;
            }
        }

        return false;
    }

    // While the text field has the focus, the picture is sized to what the on-screen keyboard leaves, with the field
    // right under it, and held at the top of what is visible. Where even that does not fit, the field is shown.
    private void FitAboveKeyboard()
    {
        if (!typing || XamlRoot is null || !picture.IsLoaded || !typeForm.IsLoaded)
        {
            return;
        }

        var keyboard = Windows.UI.ViewManagement.InputPane.GetForCurrentView().OccludedRect.Height;
        var visible = XamlRoot.Size.Height - keyboard;
        var margin = Tokens.Number("DvSpace2");
        var formBottom = typeForm.TransformToVisual(this).TransformPoint(new(0, typeForm.ActualHeight)).Y;
        var pictureBottom = picture.TransformToVisual(this).TransformPoint(new(0, picture.ActualHeight)).Y;
        var room = visible - (formBottom - pictureBottom) - (margin + margin);
        var fits = room >= typeForm.ActualHeight;

        picture.MaxWidth = fits ? room * model.PictureWidth / model.PictureHeight : double.PositiveInfinity;
        if (fits)
        {
            picture.StartBringIntoView(new() { AnimationDesired = false, VerticalAlignmentRatio = 0, VerticalOffset = margin });
        }
        else
        {
            typeForm.StartBringIntoView(new() { AnimationDesired = false, VerticalAlignmentRatio = 0.5 });
        }
    }

    #endregion
}
