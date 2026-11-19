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
                await Step("send a message", SendMessage);
                await Step("back", Back);
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
        // and the host then stays at its first width, so ask again until the content is at phone width.
        await Until("a window at phone width", () =>
        {
            if (Width() is > 0 and < 560)
            {
                return window.Content;
            }

            window.AppWindow.Resize(new(420, 860));
            return null;
        });
#endif
    }

    private async Task RestoreSignIn()
    {
        await Shown("CrewRow", startPatience);
        if (Find("SignInEmail") is not null)
        {
            throw new CheckFailedException("the saved sign-in was not restored");
        }
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

    private async Task OpenConversation()
    {
        await Press("CrewRow");
        await Shown("ComposerField");
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
            case Droid.AndroidAddressEditor box:
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
        Droid.AndroidAddressEditor => true,
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

    #endregion
}
