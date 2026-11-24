namespace Drover.Native.Controls;

/// <summary>
/// Proves that tapping any multiline input (project instructions, bot instructions, machine text area, short notes,
/// and the chat composer) as well as single-line text and password fields moves focus into the input and accepts
/// typed text into the bound value. Only compiled when <c>DroverMultilineFocusCheck=true</c> and started with
/// <c>DROVER_MULTILINE_FOCUS_CHECK=1</c>.
/// </summary>
internal static class MultilineInputFocusCheck
{
    #region Private Fields

    private static readonly ILogger logger = Log.Factory.CreateLogger(nameof(MultilineInputFocusCheck));

    private static string stage = "startup";

    #endregion

    #region Public Methods

    /// <summary>
    /// Renders single-line and multiline inputs inside a scrolling layout, taps each through the pointer path, types
    /// text through the keyboard injector, and verifies focus and bound values.
    /// </summary>
    public static void Start(Window window)
    {
        Volatile.Write(ref stage, "startup");
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            Fail($"The multiline input focus check did not finish within 30 seconds (stage: {Volatile.Read(ref stage)}).");
        });

        var sink = new Button { Content = "Focus sink", HorizontalAlignment = HorizontalAlignment.Left };
        var nameField = new Field { Label = "Name" };
        var passwordField = new Field { Label = "Password", Type = "password" };
        var projectInstructions = new TextArea { Label = "Instructions", Rows = 4 };
        var botInstructions = new TextArea { Label = "Bot instructions", Rows = 8 };
        var allowedHosts = new TextArea { Label = "Allowed hosts", Rows = 3, Machine = true };
        var notes = new TextArea { Label = "Notes", Rows = 2 };
        var composerModel = new ComposerViewModel { BotName = "Sample" };
        var composer = new Composer { Model = composerModel };

        var body = new StackPanel
        {
            Spacing = Tokens.Number("DvSpace3"),
            Padding = new(Tokens.Number("DvSpace4")),
        };
        body.Children.Add(sink);
        body.Children.Add(nameField);
        body.Children.Add(passwordField);
        body.Children.Add(projectInstructions);
        body.Children.Add(botInstructions);
        body.Children.Add(allowedHosts);
        body.Children.Add(notes);
        body.Children.Add(composer);

        var scroller = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsTabStop = false,
        };

        scroller.Loaded += (_, _) => _ = RunChecks(
            window,
            scroller,
            sink,
            nameField,
            passwordField,
            projectInstructions,
            botInstructions,
            allowedHosts,
            notes,
            composer,
            composerModel);
        window.Content = scroller;
    }

    #endregion

    #region Private Methods

    private static async Task RunChecks(
        Window window,
        ScrollViewer scroller,
        Button sink,
        Field nameField,
        Field passwordField,
        TextArea projectInstructions,
        TextArea botInstructions,
        TextArea allowedHosts,
        TextArea notes,
        Composer composer,
        ComposerViewModel composerModel)
    {
        try
        {
            await WaitForLayout(scroller, composer);
            var injector = Windows.UI.Input.Preview.Injection.InputInjector.TryCreate()
                ?? throw new InvalidOperationException("InputInjector is not available for this window.");
            injector.InitializeTouchInjection(Windows.UI.Input.Preview.Injection.InjectedInputVisualizationMode.None);

            List<string> failures = [];

            await Single_line_field_takes_focus_and_accepts_text_after_a_tap(window, injector, sink, nameField, failures);
            await Password_field_takes_focus_and_accepts_text_after_a_tap(window, injector, sink, passwordField, failures);
            await Project_instructions_text_area_takes_focus_and_accepts_text_after_a_tap(
                window, injector, sink, projectInstructions, failures);
            await Bot_instructions_text_area_takes_focus_and_accepts_text_after_a_tap(
                window, injector, sink, botInstructions, failures);
            await Machine_text_area_takes_focus_and_accepts_text_after_a_tap(
                window, injector, sink, allowedHosts, failures);
            await Notes_text_area_takes_focus_and_accepts_text_after_a_tap(window, injector, sink, notes, failures);
            await Composer_multiline_field_takes_focus_and_accepts_text_after_a_tap(
                window, injector, sink, composer, composerModel, failures);

            injector.UninitializeTouchInjection();

            if (failures.Count > 0)
            {
                Fail(string.Join(" | ", failures));
                return;
            }

            logger.LogInformation("Multiline input focus check passed.");
            Exit(0);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The multiline input focus check failed.");
            Exit(1);
        }
    }

    private static async Task Single_line_field_takes_focus_and_accepts_text_after_a_tap(
        Window window,
        Windows.UI.Input.Preview.Injection.InputInjector injector,
        Button sink,
        Field nameField,
        List<string> failures)
    {
        Volatile.Write(ref stage, "single-line Field");
        var input = First<TextBox>(nameField) ?? throw new InvalidOperationException("Name field has no TextBox.");
        var frame = First<Facet>(nameField) ?? throw new InvalidOperationException("Name field has no frame.");

        await TapAndType(
            window, injector, sink, frame, input, () => input.Text = string.Empty, 0.5, "alpha",
            "Name field (center)", () => nameField.Value, failures);
        await TapAndType(
            window, injector, sink, frame, input, () => input.Text = string.Empty, 0.15, "beta",
            "Name field (top edge)", () => nameField.Value, failures);
    }

    private static async Task Password_field_takes_focus_and_accepts_text_after_a_tap(
        Window window,
        Windows.UI.Input.Preview.Injection.InputInjector injector,
        Button sink,
        Field passwordField,
        List<string> failures)
    {
        Volatile.Write(ref stage, "password Field");
        var input = First<PasswordBox>(passwordField)
            ?? throw new InvalidOperationException("Password field has no PasswordBox.");
        var frame = First<Facet>(passwordField)
            ?? throw new InvalidOperationException("Password field has no frame.");

        await TapAndType(
            window, injector, sink, frame, input, () => input.Password = string.Empty, 0.15, "pass",
            "Password field (top edge)", () => passwordField.Value, failures);
    }

    private static async Task Project_instructions_text_area_takes_focus_and_accepts_text_after_a_tap(
        Window window,
        Windows.UI.Input.Preview.Injection.InputInjector injector,
        Button sink,
        TextArea projectInstructions,
        List<string> failures)
    {
        Volatile.Write(ref stage, "project instructions TextArea");
        var input = First<TextBox>(projectInstructions)
            ?? throw new InvalidOperationException("Project instructions has no TextBox.");
        var frame = First<Facet>(projectInstructions)
            ?? throw new InvalidOperationException("Project instructions has no frame.");

        await TapAndType(
            window, injector, sink, frame, input, () => input.Text = string.Empty, 0.15, "top",
            "Project instructions (top)", () => projectInstructions.Value, failures);
        await TapAndType(
            window, injector, sink, frame, input, () => input.Text = string.Empty, 0.5, "mid",
            "Project instructions (center)", () => projectInstructions.Value, failures);
        await TapAndType(
            window, injector, sink, frame, input, () => input.Text = string.Empty, 0.85, "low",
            "Project instructions (lower)", () => projectInstructions.Value, failures);
    }

    private static async Task Bot_instructions_text_area_takes_focus_and_accepts_text_after_a_tap(
        Window window,
        Windows.UI.Input.Preview.Injection.InputInjector injector,
        Button sink,
        TextArea botInstructions,
        List<string> failures)
    {
        Volatile.Write(ref stage, "bot instructions TextArea");
        var input = First<TextBox>(botInstructions)
            ?? throw new InvalidOperationException("Bot instructions has no TextBox.");
        var frame = First<Facet>(botInstructions)
            ?? throw new InvalidOperationException("Bot instructions has no frame.");

        await TapAndType(
            window, injector, sink, frame, input, () => input.Text = string.Empty, 0.5, "mid",
            "Bot instructions (center)", () => botInstructions.Value, failures);
        await TapAndType(
            window, injector, sink, frame, input, () => input.Text = string.Empty, 0.8, "low",
            "Bot instructions (lower)", () => botInstructions.Value, failures);
    }

    private static async Task Machine_text_area_takes_focus_and_accepts_text_after_a_tap(
        Window window,
        Windows.UI.Input.Preview.Injection.InputInjector injector,
        Button sink,
        TextArea allowedHosts,
        List<string> failures)
    {
        Volatile.Write(ref stage, "machine TextArea");
        var input = First<TextBox>(allowedHosts)
            ?? throw new InvalidOperationException("Allowed hosts has no TextBox.");
        var frame = First<Facet>(allowedHosts)
            ?? throw new InvalidOperationException("Allowed hosts has no frame.");

        await TapAndType(
            window, injector, sink, frame, input, () => input.Text = string.Empty, 0.75, "host",
            "Allowed hosts (lower)", () => allowedHosts.Value, failures);
    }

    private static async Task Notes_text_area_takes_focus_and_accepts_text_after_a_tap(
        Window window,
        Windows.UI.Input.Preview.Injection.InputInjector injector,
        Button sink,
        TextArea notes,
        List<string> failures)
    {
        Volatile.Write(ref stage, "notes TextArea");
        var input = First<TextBox>(notes) ?? throw new InvalidOperationException("Notes has no TextBox.");
        var frame = First<Facet>(notes) ?? throw new InvalidOperationException("Notes has no frame.");

        await TapAndType(
            window, injector, sink, frame, input, () => input.Text = string.Empty, 0.75, "note",
            "Notes (lower)", () => notes.Value, failures);
    }

    private static async Task Composer_multiline_field_takes_focus_and_accepts_text_after_a_tap(
        Window window,
        Windows.UI.Input.Preview.Injection.InputInjector injector,
        Button sink,
        Composer composer,
        ComposerViewModel model,
        List<string> failures)
    {
        Volatile.Write(ref stage, "Composer field");
        var input = First<TextBox>(composer) ?? throw new InvalidOperationException("Composer has no TextBox.");

        await TapAndType(
            window, injector, sink, input, input, () => input.Text = string.Empty, 0.75, "chat",
            "Composer field (lower)", () => model.Text, failures);
    }

    private static async Task TapAndType(
        Window window,
        Windows.UI.Input.Preview.Injection.InputInjector injector,
        Button sink,
        FrameworkElement targetArea,
        Control input,
        Action clearInput,
        double verticalFraction,
        string typed,
        string label,
        Func<string> readBound,
        List<string> failures)
    {
        targetArea.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        await Settle(targetArea);
        await Task.Delay(50);

        sink.Focus(FocusState.Programmatic);
        await Settle(targetArea);

        clearInput();
        await Settle(targetArea);

        var root = window.Content as UIElement ?? throw new InvalidOperationException("Window content is missing.");
        var contentElement = First<ScrollViewer>(input);
        var bounds = targetArea.TransformToVisual(root)
            .TransformBounds(new Rect(0, 0, targetArea.ActualWidth, targetArea.ActualHeight));
        var tapX = (int)Math.Round(bounds.X + (bounds.Width * 0.5));
        var tapY = (int)Math.Round(bounds.Y + (bounds.Height * verticalFraction));

        TouchTap(injector, tapX, tapY);
        await Settle(targetArea);
        await Task.Delay(50);

        if (input.FocusState is FocusState.Unfocused)
        {
            var contentHeight = contentElement?.ActualHeight ?? -1;
            var targetHeight = targetArea.ActualHeight;
            failures.Add(
                $"{label} did not take focus after tap ({tapX},{tapY}) target={targetHeight:0.#}px content={contentHeight:0.#}px.");
            return;
        }

        TypeText(injector, typed);
        var landed = await WaitFor(() => readBound() == typed ? true : null);
        if (!landed)
        {
            failures.Add(
                $"{label} took focus ({input.FocusState}) but bound value was \"{readBound()}\" instead of \"{typed}\".");
        }
    }

    private static void TouchTap(Windows.UI.Input.Preview.Injection.InputInjector injector, int x, int y)
    {
        var down = new Windows.UI.Input.Preview.Injection.InjectedInputTouchInfo
        {
            PointerInfo = new Windows.UI.Input.Preview.Injection.InjectedInputPointerInfo
            {
                PointerId = 1,
                PixelLocation = new Windows.UI.Input.Preview.Injection.InjectedInputPoint
                {
                    PositionX = x,
                    PositionY = y,
                },
                PointerOptions = Windows.UI.Input.Preview.Injection.InjectedInputPointerOptions.FirstButton
                    | Windows.UI.Input.Preview.Injection.InjectedInputPointerOptions.PointerDown
                    | Windows.UI.Input.Preview.Injection.InjectedInputPointerOptions.InContact
                    | Windows.UI.Input.Preview.Injection.InjectedInputPointerOptions.InRange,
            },
        };
        var up = new Windows.UI.Input.Preview.Injection.InjectedInputTouchInfo
        {
            PointerInfo = new Windows.UI.Input.Preview.Injection.InjectedInputPointerInfo
            {
                PointerId = 1,
                PixelLocation = new Windows.UI.Input.Preview.Injection.InjectedInputPoint
                {
                    PositionX = x,
                    PositionY = y,
                },
                PointerOptions = Windows.UI.Input.Preview.Injection.InjectedInputPointerOptions.FirstButton
                    | Windows.UI.Input.Preview.Injection.InjectedInputPointerOptions.PointerUp,
            },
        };

        injector.InjectTouchInput([down]);
        injector.InjectTouchInput([up]);
    }

    private static void TypeText(Windows.UI.Input.Preview.Injection.InputInjector injector, string text)
    {
        foreach (var character in text)
        {
            var down = new Windows.UI.Input.Preview.Injection.InjectedInputKeyboardInfo
            {
                ScanCode = character,
                KeyOptions = Windows.UI.Input.Preview.Injection.InjectedInputKeyOptions.Unicode,
            };
            var up = new Windows.UI.Input.Preview.Injection.InjectedInputKeyboardInfo
            {
                ScanCode = character,
                KeyOptions = Windows.UI.Input.Preview.Injection.InjectedInputKeyOptions.Unicode
                    | Windows.UI.Input.Preview.Injection.InjectedInputKeyOptions.KeyUp,
            };
            injector.InjectKeyboardInput([down, up]);
        }
    }

    private static async Task WaitForLayout(FrameworkElement root, FrameworkElement lastChild)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (root.IsLoaded && root.ActualHeight > 0 && lastChild.ActualHeight > 0)
            {
                await Settle(root);
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Timed out waiting for the input layout to settle.");
    }

    private static async Task<bool> WaitFor(Func<bool?> probe)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (probe() is true)
            {
                return true;
            }

            await Task.Delay(25);
        }

        return false;
    }

    private static Task Settle(UIElement root)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        root.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => done.TrySetResult());
        return done.Task;
    }

    private static T? First<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            if (First<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static void Fail(string reason)
    {
        logger.LogError("Multiline input focus check failed: {Reason}", reason);
        Exit(1);
    }

    private static void Exit(int code)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsAndroid())
        {
            ExitImmediately(code);
        }

        Environment.Exit(code);
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "_exit")]
    private static extern void ExitImmediately(int code);

    #endregion
}
