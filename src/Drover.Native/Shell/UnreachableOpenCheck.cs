namespace Drover.Native.Shell;

/// <summary>
/// Proves a deep link opened while the server is down shows the unreachable gate and does not close the app.
/// Desktop starts the app with <c>DROVER_UNREACHABLE_OPEN_CHECK=1</c>. Android starts it with the
/// <c>drover.unreachable_open</c> extra. Any other start never calls this.
/// </summary>
internal static class UnreachableOpenCheck
{
    #region Private Fields

    private static readonly Guid sampleBot = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly (string Address, Type Page)[] steps =
    [
        (Addresses.Tools, typeof(ToolConnectionsPage)),
        (Addresses.Talk(sampleBot), typeof(TalkPage)),
        (Addresses.Bot(sampleBot), typeof(BotEditorPage)),
    ];

    private static readonly HashSet<Type> seen = [];

    private static bool armed;

    private static bool prepared;

    private static int step;

    private static DateTimeOffset deadline;

    private static int finished;

    private static Window? window;

    private static Navigator? navigator;

    #endregion

    #region Public Methods

    /// <summary>
    /// Arms the check before the platform creates the application, as Android does from its launch intent.
    /// </summary>
    public static void Arm() => armed = true;

    /// <summary>Arms the check when the desktop workflow asked for it.</summary>
    public static void ArmFromEnvironment()
    {
        if (Environment.GetEnvironmentVariable("DROVER_UNREACHABLE_OPEN_CHECK") == "1")
        {
            armed = true;
        }
    }

    /// <summary>
    /// Points the new services at a server that refuses the connection, and asks the shell to open the first deep
    /// link. A normal start does nothing.
    /// </summary>
    public static void Prepare(IServiceProvider services)
    {
        if (!armed || prepared)
        {
            return;
        }

        prepared = true;
        services.GetRequiredService<ServerAddress>().Use(new Uri("https://127.0.0.1/"));
        PlatformShell.Incoming.Open(steps[0].Address);
    }

    /// <summary>
    /// Records a screen the platform opened. Ignored unless the check is armed, so a normal start pays nothing.
    /// </summary>
    public static void Seen(Type page)
    {
        if (!armed)
        {
            return;
        }

        seen.Add(page);
        Log.Factory.CreateLogger(nameof(UnreachableOpenCheck)).LogWarning(
            "Unreachable open check opened {Screen}",
            page.Name);
    }

    /// <summary>Watches until each deep link has shown the unreachable gate, then ends the process.</summary>
    public static void Start(Window open)
    {
        if (!armed)
        {
            return;
        }

        window = open;
        navigator = ((App)Application.Current).Services.GetRequiredService<Navigator>();
        deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        _ = Task.Run(() => Watch());
    }

    #endregion

    #region Private Methods

    private static async Task Watch()
    {
        var started = DateTimeOffset.UtcNow;
        while (Volatile.Read(ref finished) == 0 && DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(90))
        {
            window?.DispatcherQueue.TryEnqueue(Look);
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        if (Volatile.Read(ref finished) == 0)
        {
            Fail($"The check did not finish within 90 seconds. Opened: {Opened()}.");
        }
    }

    private static void Look()
    {
        try
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                Fail($"The {steps[step].Page.Name} screen did not show the unreachable gate. Opened: {Opened()}.");
                return;
            }

            var expected = steps[step];
            if (!seen.Contains(expected.Page) || window?.Content is not DependencyObject root)
            {
                return;
            }

            if (!Shows(root, SessionGateViewModel.UnreachableTitle))
            {
                return;
            }

            step++;
            if (step == steps.Length)
            {
                Pass();
                return;
            }

            deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
            navigator?.NavigateTo(steps[step].Address);
        }
        catch (Exception exception)
        {
            if (Interlocked.Exchange(ref finished, 1) != 0)
            {
                return;
            }

            var logger = Log.Factory.CreateLogger(nameof(UnreachableOpenCheck));
            logger.LogError(exception, "The unreachable open check failed");
            Write("Unreachable open check failed");
            NativeStop.Now(1);
        }
    }

    private static void Pass()
    {
        if (Interlocked.Exchange(ref finished, 1) != 0)
        {
            return;
        }

        var screens = string.Join(", ", steps.Select(item => item.Page.Name));
        Log.Factory.CreateLogger(nameof(UnreachableOpenCheck)).LogWarning(
            "Unreachable open check passed for {Screens}",
            screens);
        Write($"Unreachable open check passed for {screens}");
        NativeStop.Now(0);
    }

    private static void Fail(string reason)
    {
        if (Interlocked.Exchange(ref finished, 1) != 0)
        {
            return;
        }

        Log.Factory.CreateLogger(nameof(UnreachableOpenCheck)).LogError(
            "Unreachable open check failed: {Reason}",
            reason);
        Write($"Unreachable open check failed: {reason}");
        NativeStop.Now(1);
    }

    private static string Opened() => seen.Count == 0 ? "none" : string.Join(", ", seen.Select(page => page.Name));

    private static bool Shows(DependencyObject root, string text)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBlock block && block.Text == text)
            {
                return true;
            }

            if (Shows(child, text))
            {
                return true;
            }
        }

        return false;
    }

    private static void Write(string text)
    {
        var path = Environment.GetEnvironmentVariable("DROVER_UNREACHABLE_OPEN_REPORT");
#if ANDROID
        path ??= AndroidReport();
#endif
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, text);
    }

#if ANDROID
    private static string? AndroidReport()
    {
        var root = Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath;
        return root is null ? null : System.IO.Path.Combine(root, "unreachable-open.txt");
    }
#endif

    #endregion
}
