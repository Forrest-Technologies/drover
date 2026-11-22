namespace Drover.Native.Checks;

/// <summary>
/// The screen checks every target runs: the app starts on the connect screen, connects to the test server, signs
/// in, opens the conversation with the test bot, sends a message and reads the reply, and goes back to the crew.
/// Every control is found by its automation id and used through its automation peer, as assistive technology
/// would, so a missing or renamed id fails the check that needs it. Each step reports a line; the last line says
/// whether all of them passed.
/// </summary>
internal sealed class ScreenChecks(Window window, CheckSettings settings)
{
    #region Private Fields

    public const string Tag = "DroverUiCheck";

    public const string Passed = "ui-check result: passed";

    public const string Failed = "ui-check result: failed";

    private const string Message = "Hello from the screen checks.";

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan startPatience = TimeSpan.FromSeconds(120);

    // The widths a desktop window is made in turn, in pixels: the two the app is required to work at. A phone has the
    // one width it has, which is taken as it is (0).
    private static readonly int[] requiredWidths = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() ? [0] : [360, 1280];

    /// <summary>The width the checks run at when a check has made the window another.</summary>
    private const int PhoneWidth = 420;

    private static readonly TimeSpan poll = TimeSpan.FromMilliseconds(100);

    private readonly ILogger<ScreenChecks> logger = Log.For<ScreenChecks>();

    #endregion

    #region Public Methods

    public async Task Run()
    {
        var passed = false;

        try
        {
            if (settings.ChecksRestart)
            {
                await Step("restore sign-in", RestoreSignIn);
            }
            else
            {
                await Step("launch", Launch);
                await Step("connect", Connect);
                await Step("sign in", SignIn);
                await Step("open a conversation", OpenConversation);
                await Step("composer bounds", ComposerBounds);
                await Step("send a message", SendMessage);
                await Step("stop while working", StopWhileWorking);
                await Step("back", Back);
                await Step("switch the theme", SwitchTheme);
            }

            passed = true;
        }
        catch (CheckFailedException failure)
        {
            logger.LogError("The screen checks failed: {Reason}", failure.Message);
        }
        finally
        {
            Report(passed ? Passed : Failed);
            Finish();
        }
    }

    #endregion

    #region Steps

    private async Task Launch()
    {
        await Shown("ConnectAddress", startPatience);
#if !__ANDROID__ && !__IOS__
        // The native window can attach after the connect control is on screen. A resize before that is ignored,
        // and the host then stays at its first width, so ask again at a measured pace until the content is at
        // phone width without thrashing the macOS window surface.
        var resizedAt = TimeSpan.MinValue;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Until("a window at phone width", () =>
        {
            if (Width() is > 0 and < 560)
            {
                return window.Content;
            }

            if (resizedAt == TimeSpan.MinValue || watch.Elapsed - resizedAt >= TimeSpan.FromSeconds(1))
            {
                window.AppWindow.Resize(new(420, 860));
                resizedAt = watch.Elapsed;
            }

            return null;
        }, startPatience);
#endif
    }

    private async Task RestoreSignIn()
    {
        await Shown("CrewRow", startPatience);
        if (Find("SignInEmail") is not null)
        {
            throw new CheckFailedException("the saved sign-in was not restored");
        }

        var services = ((App)Application.Current).Services;
        await services.GetRequiredService<NativeSession>().Clear(settings.Server, signedOutAccess: null);
        services.GetRequiredService<ServerAddress>().Forget();
    }

    private async Task Connect()
    {
        await Type("ConnectAddress", settings.Server.AbsoluteUri);
        await Press("ConnectSubmit");
        try
        {
            await Shown("SignInEmail");
        }
        catch (CheckFailedException failure)
            when (Find("ConnectProblem") is StatusNote { Text: { Length: > 0 } problem })
        {
            throw new CheckFailedException(problem, failure);
        }
    }

    private async Task SignIn()
    {
        await Type("SignInEmail", settings.Email);
        await Type("SignInPassword", settings.Password);
        await Press("SignInSubmit");
        await Shown("CrewRow");
    }

    /// <summary>
    /// Another theme reaches the open screen without a restart, in every group that changes at run time: a color
    /// repaints in place, and the numbers (type, density, shape, bloom, motion) are what the screen is made again
    /// from. The sample is Night Yard with a different value in each group, and switching back must give exactly
    /// what was there before.
    /// </summary>
    private async Task SwitchTheme()
    {
        var theme = ((App)Application.Current).Services.GetRequiredService<ThemeModel>();
        BuiltInThemes.AddSample(SampleTheme.Value);
        var before = await Observed(Row(), "the crew before the switch");
        if (!before.TextSizes.Contains(14))
        {
            throw new CheckFailedException("Night Yard's base size is not on the open screen before the switch");
        }

        theme.Choose(new ThemeChoice(SampleTheme.Id, ThemeMode.Dark));
        var sample = await UntilSeen("the open screen made again from the sample theme", seen =>
            seen.RowMinimum == Row(sample: true) && seen.Corners == CornerLanguage.Ticked);

        Expect(sample.TextSizes.Contains(SampleTheme.BaseSize) && !sample.TextSizes.Contains(14), "type: the base step of the scale");
        Expect(sample.Space == SampleTheme.SpaceFour, "density: the spacing multiplier");
        Expect(sample.FrameCut == SampleTheme.FrameCut, "shape: the size of a frame's corner");
        Expect(sample.Corners == CornerLanguage.Ticked, "shape: the corner language of the frames");
        Expect(sample.Bloom == SampleTheme.Bloom && sample.Duration == SampleTheme.Duration, "motion: bloom and the duration scale");
        Expect(Motion.Fades || Motion.IsReduced, "motion: a live state fades instead of looping");

        // The yard is drawn only where the screen is wide enough to show the working area, so it is read when it was drawn.
        Expect(sample.Yard.Step == 0 || sample.Yard == (YardTexture.Grid, SampleTheme.YardStep), "surfaces: the yard texture and step");
        Expect(sample.CharacterLine == SampleTheme.CharacterLine, "characters: the part line weight");
        Expect(sample.Ember != before.Ember, "color: the accent repaints");

        theme.Choose(ThemeChoice.Default with { Mode = ThemeMode.Dark });
        var back = await UntilSeen("the open screen made again from Night Yard", seen =>
            seen.RowMinimum == before.RowMinimum && seen.Corners == before.Corners);
        Expect(back == before, "Night Yard is exactly what it was before");
    }

    // Waits for the open screen to show what is asked, and says what it showed last when it never does.
    private async Task<Seen> UntilSeen(string what, Func<Seen, bool> holds)
    {
        try
        {
            return await Until(what, () => Observe() is { } seen && holds(seen) ? seen : null);
        }
        catch (CheckFailedException failure)
        {
            var last = Observe();
            var sizes = last is null ? string.Empty : string.Join(", ", last.TextSizes.Order());
            throw new CheckFailedException(
                $"{failure.Message}; last seen: {(last is null ? "no crew row" : $"row {last.RowMinimum}, corners {last.Corners}, frame {last.FrameCut}, space {last.Space}, text sizes {sizes}")}",
                failure);
        }
    }

    private async Task<Seen> Observed(double row, string what) =>
        await Until(what, () => Observe() is { } seen && seen.RowMinimum == row ? seen : null);

    private static double Row(bool sample = false) =>
        sample ? (Targets.IsCoarse ? SampleTheme.RowTouch : SampleTheme.RowPointer) : Targets.Row;

    private Seen? Observe()
    {
        var roots = Roots().SelectMany(root => Descendants(root).Prepend(root)).ToList();
        if (Find("CrewRow") is not { } row)
        {
            return null;
        }

        var sizes = roots.OfType<TextBlock>().Select(block => block.FontSize).ToHashSet();
        var frames = roots.OfType<Facet>().ToList();
        var yard = roots.OfType<Yard>().FirstOrDefault()?.Drawn ?? default;

        return new(
            row.MinHeight, sizes, Tokens.Number("DvSpace4"), frames.Count == 0 ? 0 : frames.Max(frame => frame.DrawnSize),
            frames.Count == 0 ? CornerLanguage.Faceted : frames[0].DrawnCorners, Tokens.Number("DvBloomIon"), Tokens.Number("DvDur2"),
            yard, Tokens.Number("DvCharacterLine"), Tokens.Color(row, "DvEmber"));
    }

    private void Expect(bool holds, string what)
    {
        if (!holds)
        {
            throw new CheckFailedException($"the theme did not change {what}");
        }
    }

    private async Task OpenConversation()
    {
        await Press("CrewRow");
        // The composer is visible while its conversation loads, but a person cannot type until it is enabled.
        await Until("ComposerField to be enabled", () =>
            Find("ComposerField") is TextBox { IsEnabled: true } field ? field : null);
    }

    /// <summary>
    /// The automation box of each composer button is where the button is drawn, within 2 px, at both widths the app
    /// is required to work at, with the options row open and again once the field is three lines tall, which moves
    /// everything above it. The browser build keeps its accessible boxes apart from the drawn controls, so
    /// <c>check-native-browser.mjs</c> asks the canvas; here the peer's rectangle is compared with the control's own.
    /// </summary>
    private async Task ComposerBounds()
    {
        await Shown("ComposerField");

        foreach (var width in requiredWidths)
        {
            await AtWidth(width);
            await Press("ComposerMore");
            await AreWhereDrawn($"at {Width():0} px with the options open", "ComposerMore", "ComposerAttach", "ComposerSend");
            var oneLine = (await Shown("ComposerField")).ActualHeight;
            await Type("ComposerField", "one\ntwo\nthree");
            await Until("the field to grow", () => Find("ComposerField") is { } grown && grown.ActualHeight > oneLine + 20 ? grown : null);
            await AreWhereDrawn($"at {Width():0} px with the field three lines tall", "ComposerMore", "ComposerAttach", "ComposerSend");
            await Type("ComposerField", string.Empty);
            await Press("ComposerMore");
        }

        await AtWidth(PhoneWidth);
    }

    /// <summary>
    /// While a run is in progress Stop stands where Send was. The check server's model holds its second run until it
    /// is stopped, so this sends a message, looks at Stop and the options button at both required widths, and then
    /// presses Stop, which ends the run and brings Send back.
    /// </summary>
    private async Task StopWhileWorking()
    {
        await Type("ComposerField", "hold this run");
        await Press("ComposerSend");
        await Shown("ComposerStop");

        foreach (var width in requiredWidths)
        {
            await AtWidth(width);
            await AreWhereDrawn($"at {Width():0} px with a run in progress", "ComposerMore", "ComposerStop");
        }

        await AtWidth(PhoneWidth);
        await Press("ComposerStop");
        await Until("Send to come back once the run is stopped", () => Find("ComposerStop") is null ? Find("ComposerSend") : null);
    }

    /// <summary>
    /// Makes the window <paramref name="width"/> wide. A resize asked for before the window is ready can be undone by
    /// the host's own first size, and asking for the same size again changes nothing, so each ask differs from the
    /// last by a few pixels. A window can refuse a width (a smallest size a system keeps a window above, a screen
    /// smaller than the width), so after a few seconds the width it gave is taken if it is in the same layout, phone
    /// or wide, as the width asked for: what the check is about is that layout.
    /// </summary>
    private async Task AtWidth(int width)
    {
        if (width == 0)
        {
            return;
        }

#if !__ANDROID__ && !__IOS__
        var asked = 0;
        var resizedAt = TimeSpan.MinValue;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Until($"a window {width} px wide", () =>
        {
            var phone = (double)Styles.Dictionary["DvBreakPhone"];
            if (Math.Abs(Width() - width) <= 8
                || (watch.Elapsed > TimeSpan.FromSeconds(6) && Width() > 0 && (Width() < phone) == (width < phone)))
            {
                return window.Content;
            }

            if (resizedAt == TimeSpan.MinValue || watch.Elapsed - resizedAt >= TimeSpan.FromSeconds(1))
            {
                window.AppWindow.Resize(new(width + (asked++ % 2 * 4), 860));
                resizedAt = watch.Elapsed;
            }

            return null;
        });

        // The content takes its new layout on the frames after the window reaches its width: look until it is the
        // same twice running.
        var lastWidth = -1d;
        await Until("the layout to settle", () =>
        {
            var now = (window.Content as FrameworkElement)?.ActualWidth ?? 0;
            var settled = Math.Abs(now - lastWidth) < 0.5;
            lastWidth = now;
            return settled ? window.Content : null;
        });
#else
        await Task.CompletedTask;
#endif
    }

    private async Task AreWhereDrawn(string state, params string[] ids)
    {
        var root = window.Content as FrameworkElement ?? throw new CheckFailedException("the window has no content");

        foreach (var id in ids)
        {
            var element = await Shown(id);
            var drawn = element.TransformToVisual(root).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(element)
                ?? throw new CheckFailedException($"{id} has no automation peer");
            var box = peer.GetBoundingRectangle();

            if (Math.Abs(box.X - drawn.X) > 2 || Math.Abs(box.Y - drawn.Y) > 2
                || Math.Abs(box.Width - drawn.Width) > 2 || Math.Abs(box.Height - drawn.Height) > 2)
            {
                var said = $"its automation box is {box.X:0.#},{box.Y:0.#} {box.Width:0.#}x{box.Height:0.#}";
                throw new CheckFailedException(
                    $"{id} {state}: {said} but it is drawn at {drawn.X:0.#},{drawn.Y:0.#} {drawn.Width:0.#}x{drawn.Height:0.#}");
            }
        }
    }

    private async Task SendMessage()
    {
        await Type("ComposerField", Message);
        await Press("ComposerSend");
        await Until($"the reply \"{settings.Reply}\" in MessageList", () =>
            Find("MessageList") is { } list && Words(list).Any(words => words.Contains(settings.Reply, StringComparison.Ordinal))
                ? list
                : null);
    }

    private async Task Back()
    {
        await Press("TalkBack");
        await Until("the crew without the conversation", () =>
            Find("CrewRow") is { } row && Find("ComposerField") is null ? row : null);
    }

    #endregion

    #region Actions

    private double Width() => (window.Content as FrameworkElement)?.ActualWidth ?? 0;

    private async Task Step(string name, Func<Task> step)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await step();
        }
        catch (CheckFailedException failure)
        {
            Report($"ui-check fail: {name}: {failure.Message}");
            throw;
        }
        catch (Exception exception)
        {
            Report($"ui-check fail: {name}: {exception}");
            throw new CheckFailedException($"{name} threw {exception.GetType().Name}", exception);
        }

        Report($"ui-check pass: {name} ({watch.ElapsedMilliseconds} ms)");
    }

    private Task<FrameworkElement> Shown(string id, TimeSpan? wait = null) => Until(id, () => Find(id), wait);

    /// <summary>Puts the text in the editor with the id, or the editor inside it, as a person typing would leave it.</summary>
    private async Task Type(string id, string text)
    {
        var target = await Shown(id);

        switch (Descendants(target).Prepend(target).FirstOrDefault(IsEditor))
        {
            case TextBox box:
                box.Text = text;
                break;
            case PasswordBox box:
                box.Password = text;
                break;
#if __ANDROID__
            case Droid.AndroidTextEditor box:
                box.Text = text;
                break;
#endif
            default:
                throw new CheckFailedException($"{id} holds no text editor");
        }

        // A field passes what was typed on to its form when the editor says it changed.
        if (target is Field field)
        {
            await Until($"{id} to hold what was typed", () => field.Value == text ? field : null);
        }
    }

    /// <summary>Presses the control with the id through its automation peer, once it can be pressed.</summary>
    private async Task Press(string id)
    {
        var target = await Until($"{id} to be enabled", () =>
            Find(id) is Microsoft.UI.Xaml.Controls.Button { IsEnabled: true } button ? button : null);

        if (FrameworkElementAutomationPeer.CreatePeerForElement(target)?.GetPattern(PatternInterface.Invoke)
            is not Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider invoker)
        {
            throw new CheckFailedException($"{id} cannot be pressed through its automation peer");
        }

        invoker.Invoke();
    }

    // The next look is a timer tick rather than a delay so the check keeps running on the window's dispatcher.
    private Task<T> Until<T>(string what, Func<T?> probe, TimeSpan? wait = null)
        where T : class
    {
        if (probe() is { } already)
        {
            return Task.FromResult(already);
        }

        var found = new TaskCompletionSource<T>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var limit = wait ?? patience;
        var queue = window.DispatcherQueue.CreateTimer();
        queue.Interval = poll;
        queue.IsRepeating = true;
        queue.Tick += (_, _) => Look(found, watch, limit, what, probe, queue.Stop);
        queue.Start();
        return found.Task;
    }

    private void Look<T>(
        TaskCompletionSource<T> found,
        System.Diagnostics.Stopwatch watch,
        TimeSpan limit,
        string what,
        Func<T?> probe,
        Action stop)
        where T : class
    {
        try
        {
            if (probe() is { } hit)
            {
                stop();
                found.TrySetResult(hit);
                return;
            }

            if (watch.Elapsed > limit)
            {
                stop();
                found.TrySetException(new CheckFailedException($"waited {limit.TotalSeconds:0} s for {what}"));
            }
        }
        catch (Exception exception)
        {
            stop();
            logger.LogError(exception, "The screen checks could not look at the page.");
            found.TrySetException(exception);
        }
    }

    #endregion

    #region The Tree

    /// <summary>The element on screen with the automation id, in the window or in an open popup.</summary>
    private FrameworkElement? Find(string id) => Roots()
        .SelectMany(root => Descendants(root).Prepend(root))
        .OfType<FrameworkElement>()
        .FirstOrDefault(element => AutomationIds.Matches(AutomationProperties.GetAutomationId(element), IsShown(element), id));

    private IEnumerable<DependencyObject> Roots()
    {
        if (window.Content is not { } content)
        {
            return [];
        }

        var popups = content.XamlRoot is { } root ? VisualTreeHelper.GetOpenPopupsForXamlRoot(root) : [];
        return popups.Select(popup => popup.Child).OfType<DependencyObject>().Prepend(content);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var waiting = new Stack<DependencyObject>([root]);

        while (waiting.TryPop(out var next))
        {
            for (var index = VisualTreeHelper.GetChildrenCount(next) - 1; index >= 0; index--)
            {
                var child = VisualTreeHelper.GetChild(next, index);
                waiting.Push(child);
                yield return child;
            }
        }
    }

    private static bool IsShown(FrameworkElement element)
    {
        if (!element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        for (DependencyObject? at = element; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (at is UIElement { Visibility: Visibility.Collapsed })
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsEditor(DependencyObject element) => element switch
    {
        TextBox or PasswordBox => true,
#if __ANDROID__
        Droid.AndroidTextEditor => true,
#endif
        _ => false,
    };

    /// <summary>The words drawn inside the element, one entry for each piece of text.</summary>
    private static IEnumerable<string> Words(DependencyObject root) => Descendants(root).Select(element => element switch
    {
        TextBlock block => block.Text,
        RichTextBlock block => string.Concat(block.Blocks
            .OfType<Microsoft.UI.Xaml.Documents.Paragraph>()
            .SelectMany(paragraph => Runs(paragraph.Inlines))),
        _ => string.Empty,
    }).Where(words => words.Length > 0);

    private static IEnumerable<string> Runs(IEnumerable<Microsoft.UI.Xaml.Documents.Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Microsoft.UI.Xaml.Documents.Run run)
            {
                yield return run.Text;
            }
            else if (inline is Microsoft.UI.Xaml.Documents.Span span)
            {
                foreach (var words in Runs(span.Inlines))
                {
                    yield return words;
                }
            }
        }
    }

    #endregion

    #region Reporting

    private void Report(string line)
    {
#if __ANDROID__
        var entry = string.IsNullOrEmpty(settings.RunId) ? line : $"{settings.RunId} {line}";
        Android.Util.Log.Info(Tag, entry);
#else
        if (settings.ReportPath is { } path)
        {
            File.AppendAllText(path, $"{line}\n");
        }
#endif
        logger.LogWarning("{Line}", line);
    }

    // A desktop app closes, which tells the harness the report is complete. Android and iOS stay open: the harness
    // reads the result and stops the app itself.
    private static void Finish()
    {
#if !__ANDROID__ && !__IOS__
        Application.Current.Exit();
#endif
    }

    #endregion

    #region Nested Types

    private sealed class CheckFailedException(string message, Exception? inner = null) : Exception(message, inner);

    /// <summary>What a check read off the open screen: one value from each group the theme changes at run time.</summary>
    private sealed record Seen(
        double RowMinimum,
        HashSet<double> TextSizes,
        double Space,
        double FrameCut,
        CornerLanguage Corners,
        double Bloom,
        double Duration,
        (YardTexture Texture, double Step) Yard,
        double CharacterLine,
        Color Ember)
    {
        public bool Equals(Seen? other) =>
            other is not null && RowMinimum == other.RowMinimum && TextSizes.SetEquals(other.TextSizes) && Space == other.Space
            && FrameCut == other.FrameCut && Corners == other.Corners && Bloom == other.Bloom && Duration == other.Duration
            && CharacterLine == other.CharacterLine && Ember == other.Ember;

        public override int GetHashCode() => HashCode.Combine(RowMinimum, Space, FrameCut, Bloom, Duration, Ember);
    }

    #endregion
}
