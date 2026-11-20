namespace Drover.Native.Shell;

/// <summary>
/// Proves a deep link opened while the server is down catches the open failure, shows the unreachable gate, and raises
/// no unhandled error. Desktop starts the app with <c>DROVER_UNREACHABLE_OPEN_CHECK=1</c>. Android starts it with the
/// <c>drover.unreachable_open</c> extra. Any other start is not compiled with this type.
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

    private static bool watchingUnhandled;

    private static int step;

    private static int caughtOpens;

    private static DateTimeOffset deadline;

    private const int PollMilliseconds = 200;

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

    /// <summary>Hooks unhandled-exception listeners on the application and domain before any screen opens.</summary>
    public static void WatchUnhandled(Application app)
    {
        if (watchingUnhandled)
        {
            return;
        }

        watchingUnhandled = true;
        app.UnhandledException += (_, failure) => OnUnhandled("Application.UnhandledException", failure.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, failure) =>
            OnUnhandled("AppDomain.UnhandledException", failure.ExceptionObject as Exception);
    }

    /// <summary>
    /// Arms the log listener, points the new services at a server that refuses the connection, and asks the shell to
    /// open the first deep link. A normal start does nothing.
    /// </summary>
    public static void Prepare(IServiceProvider services)
    {
        if (!armed || prepared)
        {
            return;
        }

        prepared = true;
        services.GetRequiredService<ILoggerFactory>().AddProvider(new CheckLogProvider());
#if DROVER_BROWSER
        // A browser has no device shell and no saved server. This check runs on desktop and Android.
        _ = services;
#else
        services.GetRequiredService<ServerAddress>().Use(new Uri("https://127.0.0.1/"));
        PlatformShell.Incoming.Open(steps[0].Address);
#endif
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

    private static void OnUnhandled(string source, Exception? exception)
    {
        if (!armed)
        {
            return;
        }

        var detail = exception is null ? "unknown error" : $"{exception.GetType().Name}: {exception.Message}";
        Fail($"{source} raised {detail}");
    }

    private static async Task Watch()
    {
        var started = DateTimeOffset.UtcNow;
        while (Volatile.Read(ref finished) == 0 && DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(90))
        {
            window?.DispatcherQueue.TryEnqueue(Look);
            await Task.Delay(TimeSpan.FromMilliseconds(PollMilliseconds));
        }

        if (Volatile.Read(ref finished) == 0)
        {
            Fail($"The check did not finish within 90 seconds. Opened: {Opened()}. Caught opens: {Volatile.Read(ref caughtOpens)}.");
        }
    }

    private static void Look()
    {
        try
        {
            if (Volatile.Read(ref finished) != 0)
            {
                return;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                Fail($"The {steps[step].Page.Name} screen did not show the caught unreachable gate. Opened: {Opened()}. Caught opens: {Volatile.Read(ref caughtOpens)}.");
                return;
            }

            var expected = steps[step];
            if (!seen.Contains(expected.Page)
                || Volatile.Read(ref caughtOpens) < step + 1
                || window?.Content is not DependencyObject root)
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
                var totalCaught = Volatile.Read(ref caughtOpens);
                if (totalCaught != steps.Length)
                {
                    Fail($"Expected {steps.Length} caught screen opens, saw {totalCaught}.");
                    return;
                }

                Pass();
                return;
            }

            deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
            navigator?.NavigateTo(steps[step].Address);
        }
        catch (Exception exception)
        {
            Log.Factory.CreateLogger(nameof(UnreachableOpenCheck)).LogError(
                exception,
                "The unreachable open check threw {ExceptionType}",
                exception.GetType().Name);
            Fail($"The unreachable open check threw {exception.GetType().Name}: {exception.Message}");
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
        Exit(0);
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

    #region Nested Types

    private sealed class CheckLogProvider : ILoggerProvider
    {
        #region Interface Implementations

        public ILogger CreateLogger(string categoryName) => new CheckLogger(categoryName);

        public void Dispose()
        {
        }

        #endregion
    }

    private sealed class CheckLogger(string categoryName) : ILogger
    {
        #region Interface Implementations

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => armed && logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!armed)
            {
                return;
            }

            if (logLevel >= LogLevel.Error
                && (categoryName.StartsWith("Uno.UI.Dispatching.NativeDispatcher", StringComparison.Ordinal)
                    || categoryName == typeof(App).FullName))
            {
                Fail($"Error logged by {categoryName}: {formatter(state, exception)}");
                return;
            }

            if (logLevel == LogLevel.Warning
                && formatter(state, exception).StartsWith(
                    "A screen could not reach the server while it opened",
                    StringComparison.Ordinal))
            {
                Interlocked.Increment(ref caughtOpens);
            }
        }

        #endregion
    }

    #endregion
}
