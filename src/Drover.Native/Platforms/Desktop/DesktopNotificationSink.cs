namespace Drover.Native.Platform;

/// <summary>Shows one system notification and calls back when the person selects it.</summary>
internal sealed class DesktopNotificationSink(
    DesktopNotificationSelections selections,
    ILogger<DesktopNotificationSink> logger) : IDisposable
{
    #region Private Fields

    private readonly ISystemNotification notifications = Make(selections.Open, logger);

    private readonly object gate = new();

    private long currentVersion;

    private bool disposed;

    #endregion

    #region Properties

    public bool IsAvailable => notifications is not UnavailableSystemNotification;

    public event Action<string, long>? Selected
    {
        add => selections.Selected += value;
        remove => selections.Selected -= value;
    }

    #endregion

    #region Public Methods

    public void Show(string body, string address, long version)
    {
        lock (gate)
        {
            if (!disposed && version == currentVersion)
            {
                notifications.Show(body, address, version);
            }
        }
    }

    public void Clear(long version)
    {
        lock (gate)
        {
            currentVersion = version;
            notifications.Clear();
        }
    }

    #endregion

    #region Interface Implementations

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            notifications.Dispose();
        }
    }

    #endregion

    #region Private Methods

    private static ISystemNotification Make(Action<string, long> open, ILogger logger)
    {
        try
        {
            return OperatingSystem.IsWindows()
                ? new WindowsSystemNotification(open)
                : OperatingSystem.IsMacOS()
                    ? new MacSystemNotification(open)
                    : new LinuxSystemNotification(open, logger);
        }
        catch (Exception exception) when (exception is IOException or TypeInitializationException
            or DllNotFoundException or EntryPointNotFoundException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(exception, "System notifications are unavailable on this desktop");
            return new UnavailableSystemNotification();
        }
    }

    #endregion
}


internal sealed class DesktopNotificationSelections
{
    #region Properties

    public event Action<string, long>? Selected;

    #endregion

    #region Public Methods

    public void Open(string address, long version) => Selected?.Invoke(address, version);

    #endregion
}


internal interface ISystemNotification : IDisposable
{
    void Show(string body, string address, long version);

    void Clear();
}


internal sealed class UnavailableSystemNotification : ISystemNotification
{
    #region Interface Implementations

    public void Show(string body, string address, long version)
    {
    }

    public void Clear()
    {
    }

    public void Dispose()
    {
    }

    #endregion
}


/// <summary>Uses the notification icon API that is present on supported desktop versions.</summary>
internal sealed class WindowsSystemNotification : ISystemNotification
{
    #region Private Fields

    private const uint CallbackMessage = 0x8001;

    private const uint Add = 0;

    private const uint Delete = 2;

    private const uint SetVersion = 4;

    private const uint MessageFlag = 1;

    private const uint IconFlag = 2;

    private const uint TipFlag = 4;

    private const uint InfoFlag = 16;

    private const uint Version = 4;

    private const uint BalloonSelected = 0x405;

    private const uint BalloonHidden = 0x403;

    private const uint BalloonTimedOut = 0x404;

    private const uint IconSelected = 0x400;

    private const int ClassAlreadyExists = 1410;

    private const string WindowClassName = "DroverNotificationWindow";

    private static readonly ConcurrentDictionary<nint, WindowsSystemNotification> windows = [];

    private static readonly WindowProcedure windowProcedure = Receive;

    private readonly nint window;

    private readonly Action<string, long> open;

    private readonly ConcurrentDictionary<uint, WindowsDestination> destinations = [];

    private int nextIcon;

    private long sequence;

    private bool disposed;

    #endregion

    #region Constructors

    public WindowsSystemNotification(Action<string, long> open)
    {
        this.open = open;
        var instance = GetModuleHandle(null);
        var windowClass = new WindowClass
        {
            Procedure = windowProcedure,
            Instance = instance,
            ClassName = WindowClassName,
        };

        if (RegisterClass(ref windowClass) == 0 && Marshal.GetLastWin32Error() != ClassAlreadyExists)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        window = CreateWindowEx(
            0,
            WindowClassName,
            "Drover notifications",
            0,
            0,
            0,
            0,
            0,
            new(-3),
            nint.Zero,
            instance,
            nint.Zero);
        if (window == nint.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        windows[window] = this;
    }

    #endregion

    #region Interface Implementations

    public void Show(string body, string address, long version)
    {
        var id = NextIconId();
        destinations[id] = new(id, address, version, Interlocked.Increment(ref sequence));
        var shown = Data(id, MessageFlag | IconFlag | TipFlag | InfoFlag);
        shown.Icon = LoadIcon(nint.Zero, new(32512));
        shown.Tip = "Drover";
        shown.InfoTitle = "Drover";
        shown.Info = NotificationText.Cut(body, NotificationText.MaxCodeUnits);
        if (!ShellNotifyIcon(Add, ref shown))
        {
            destinations.TryRemove(id, out _);
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        shown.TimeoutOrVersion = Version;
        ShellNotifyIcon(SetVersion, ref shown);
        Trim();
    }

    public void Clear()
    {
        foreach (var id in destinations.Keys)
        {
            Remove(id);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Clear();
        windows.TryRemove(window, out _);
        DestroyWindow(window);
    }

    #endregion

    #region Native Messages

    private NotificationIconData Data(uint id, uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<NotificationIconData>(),
        Window = window,
        Id = id,
        Flags = flags,
        CallbackMessage = CallbackMessage,
        Tip = string.Empty,
        Info = string.Empty,
        InfoTitle = string.Empty,
    };

    private static nint Receive(nint window, uint message, nuint word, nint data)
    {
        if (message == CallbackMessage && windows.TryGetValue(window, out var notification))
        {
            var packed = unchecked((uint)data.ToInt64());
            var eventCode = packed & 0xffff;
            var iconId = packed >> 16;
            if (iconId == 0)
            {
                iconId = unchecked((uint)word);
            }

            if (eventCode is BalloonSelected or IconSelected
                && notification.destinations.TryGetValue(iconId, out var destination))
            {
                notification.open(destination.Address, destination.Version);
            }

            if (eventCode is BalloonSelected or BalloonHidden or BalloonTimedOut or IconSelected)
            {
                notification.Remove(iconId);
            }
        }

        return DefWindowProc(window, message, word, data);
    }

    private uint NextIconId()
    {
        uint id;
        do
        {
            id = (uint)(Interlocked.Increment(ref nextIcon) & ushort.MaxValue);
        }
        while (id == 0 || destinations.ContainsKey(id));

        return id;
    }

    private void Remove(uint id)
    {
        if (destinations.TryRemove(id, out _))
        {
            var icon = Data(id, 0);
            ShellNotifyIcon(Delete, ref icon);
        }
    }

    private void Trim()
    {
        var excess = Math.Max(0, destinations.Count - 32);
        foreach (var old in destinations.Values.OrderBy(value => value.Sequence).Take(excess))
        {
            Remove(old.Id);
        }
    }

    #endregion

    #region Native Types

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        #region Properties

        public uint Style;
        public WindowProcedure Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;

        #endregion
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotificationIconData
    {
        #region Properties

        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;

        #endregion
    }

    private delegate nint WindowProcedure(nint window, uint message, nuint word, nint data);

    private sealed record WindowsDestination(uint Id, string Address, long Version, long Sequence);

    #endregion

    #region Native Calls

    [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32", EntryPoint = "RegisterClassW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WindowClass windowClass);

    [DllImport("user32", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32", EntryPoint = "DefWindowProcW")]
    private static extern nint DefWindowProc(nint window, uint message, nuint word, nint data);

    [DllImport("user32", EntryPoint = "LoadIconW", CharSet = CharSet.Unicode)]
    private static extern nint LoadIcon(nint instance, nint iconName);

    [DllImport("shell32", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotificationIconData data);

    #endregion
}


/// <summary>
/// Uses the notification center available to desktop apps and receives selection through its delegate.
/// </summary>
internal sealed class MacSystemNotification(Action<string, long> open) : ISystemNotification
{
    #region Private Fields

    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    private static readonly ConcurrentDictionary<string, MacDestination> destinations = [];

    private static readonly NotificationSelected selected = Selected;

    private static readonly NotificationSelected dismissed = Dismissed;

    private static readonly NotificationVisible visible = Visible;

    private static readonly nint identifierKey = Send(NativeString("drover.notification"), Selector("retain"));

    private static readonly nint center = MakeCenter();

    private static long sequence;

    private readonly nint availableCenter = center != nint.Zero
        ? center
        : throw new IOException("The system notification center is unavailable.");

    #endregion

    #region Interface Implementations

    public void Show(string body, string address, long version)
    {
        var id = Guid.NewGuid().ToString("N");
        var notification = Send(Send(Class("NSUserNotification"), Selector("alloc")), Selector("init"));
        Send(notification, Selector("setTitle:"), NativeString("Drover"));
        Send(notification, Selector("setInformativeText:"), NativeString(body));
        var userInfo = Send(
            Class("NSDictionary"),
            Selector("dictionaryWithObject:forKey:"),
            NativeString(id),
            identifierKey);
        Send(notification, Selector("setUserInfo:"), userInfo);
        destinations[id] = new(id, notification, open, address, version, Interlocked.Increment(ref sequence));
        Send(availableCenter, Selector("deliverNotification:"), notification);
        Trim();
    }

    public void Clear()
    {
        foreach (var id in destinations.Keys)
        {
            Remove(id, invoke: false);
        }
    }

    public void Dispose()
    {
        Clear();
    }

    #endregion

    #region Delegate Construction

    private static nint MakeCenter()
    {
        var delegateClass = AllocateClass(Class("NSObject"), "DroverNotificationDelegate", 0);
        AddMethod(
            delegateClass,
            Selector("userNotificationCenter:didActivateNotification:"),
            Marshal.GetFunctionPointerForDelegate(selected),
            "v@:@@");
        AddMethod(
            delegateClass,
            Selector("userNotificationCenter:didDismissAlert:"),
            Marshal.GetFunctionPointerForDelegate(dismissed),
            "v@:@@");
        AddMethod(
            delegateClass,
            Selector("userNotificationCenter:shouldPresentNotification:"),
            Marshal.GetFunctionPointerForDelegate(visible),
            "B@:@@");
        RegisterClass(delegateClass);

        var instance = Send(Send(delegateClass, Selector("alloc")), Selector("init"));
        var notificationCenter = Send(Class("NSUserNotificationCenter"), Selector("defaultUserNotificationCenter"));
        Send(notificationCenter, Selector("setDelegate:"), instance);
        return notificationCenter;
    }

    private static void Selected(nint self, nint command, nint notificationCenter, nint notification)
    {
        if (Identifier(notification) is { } id)
        {
            Remove(id, invoke: true);
        }
    }

    private static void Dismissed(nint self, nint command, nint notificationCenter, nint notification)
    {
        if (Identifier(notification) is { } id)
        {
            Remove(id, invoke: false);
        }
    }

    private static byte Visible(nint self, nint command, nint notificationCenter, nint notification) => 1;

    private static nint NativeString(string value)
    {
        var bytes = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return Send(Class("NSString"), Selector("stringWithUTF8String:"), bytes);
        }
        finally
        {
            Marshal.FreeCoTaskMem(bytes);
        }
    }

    private static string? Identifier(nint notification)
    {
        var userInfo = Send(notification, Selector("userInfo"));
        var identifier = Send(userInfo, Selector("objectForKey:"), identifierKey);
        var bytes = Send(identifier, Selector("UTF8String"));
        return bytes == nint.Zero ? null : Marshal.PtrToStringUTF8(bytes);
    }

    private static void Remove(string id, bool invoke)
    {
        if (!destinations.TryRemove(id, out var destination))
        {
            return;
        }

        Send(center, Selector("removeDeliveredNotification:"), destination.Notification);
        Send(destination.Notification, Selector("release"));
        if (invoke)
        {
            destination.Open(destination.Address, destination.Version);
        }
    }

    private static void Trim()
    {
        var excess = Math.Max(0, destinations.Count - 32);
        foreach (var old in destinations.Values.OrderBy(value => value.Sequence).Take(excess))
        {
            Remove(old.Id, invoke: false);
        }
    }

    #endregion

    #region Native Types

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NotificationSelected(nint self, nint command, nint notificationCenter, nint notification);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte NotificationVisible(nint self, nint command, nint notificationCenter, nint notification);

    private sealed record MacDestination(
        string Id,
        nint Notification,
        Action<string, long> Open,
        string Address,
        long Version,
        long Sequence);

    #endregion

    #region Native Calls

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_getClass")]
    private static extern nint Class(string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
    private static extern nint Selector(string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_allocateClassPair")]
    private static extern nint AllocateClass(nint superclass, string name, nuint extraBytes);

    [DllImport(ObjectiveCLibrary, EntryPoint = "class_addMethod")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddMethod(nint targetClass, nint selector, nint implementation, string types);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_registerClassPair")]
    private static extern void RegisterClass(nint targetClass);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint argument);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint first, nint second);

    #endregion
}


/// <summary>Uses the desktop session bus notification service and follows its selection and close signals.</summary>
internal sealed class LinuxSystemNotification : ISystemNotification
{
    #region Private Fields

    private const string Destination = "org.freedesktop.Notifications";

    private const string Interface = "org.freedesktop.Notifications";

    private const string Path = "/org/freedesktop/Notifications";

    private const string DefaultAction = "default";

    private static readonly string[] actions = [DefaultAction, "Open conversation"];

    private static readonly KeyValuePair<string, VariantValue>[] hints = [];

    private static readonly TimeSpan shutdownLimit = TimeSpan.FromSeconds(2);

    private readonly Action<string, long> open;

    private readonly ILogger logger;

    private readonly DBusConnection connection;

    private readonly Dictionary<uint, LinuxDestination> destinations = [];

    private readonly HashSet<Task> pending = [];

    private readonly object gate = new();

    private Task? observing;

    private IDisposable? actionObserver;

    private IDisposable? closedObserver;

    private long epoch;

    private long sequence;

    private bool? bodyMarkup;

    private bool disposed;

    #endregion

    #region Constructors

    public LinuxSystemNotification(Action<string, long> open, ILogger logger)
    {
        this.open = open;
        this.logger = logger;
        var address = DBusAddress.Session
            ?? throw new IOException("The desktop session bus is unavailable.");
        connection = new(address);
    }

    #endregion

    #region Interface Implementations

    public void Show(string body, string address, long version)
    {
        long current;
        lock (gate)
        {
            current = epoch;
        }

        Track(() => Send(body, address, version, current));
    }

    public void Clear()
    {
        uint[] ids;
        lock (gate)
        {
            epoch++;
            ids = [.. destinations.Keys];
            destinations.Clear();
        }

        foreach (var id in ids)
        {
            Track(() => CloseQuietly(id));
        }
    }

    public void Dispose()
    {
        IDisposable? selected;
        IDisposable? closed;
        Task[] operations;
        uint[] ids;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            selected = actionObserver;
            closed = closedObserver;
            actionObserver = null;
            closedObserver = null;
            operations = [.. pending];
            ids = [.. destinations.Keys];
            destinations.Clear();
        }

        _ = ShutDown(operations, ids, selected, closed);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Starts a bus call that shutting down waits for, so its notification is closed before the connection is.
    /// Nothing starts once the connection is closing.
    /// </summary>
    private void Track(Func<Task> work)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            pending.Add(completion.Task);
        }

        _ = RunTracked(work, completion);
    }

    private async Task RunTracked(Func<Task> work, TaskCompletionSource completion)
    {
        try
        {
            await work();
        }
        finally
        {
            completion.TrySetResult();
            lock (gate)
            {
                pending.Remove(completion.Task);
            }
        }
    }

    private async Task Send(string body, string address, long version, long shownEpoch)
    {
        try
        {
            await EnsureObserving();
            if (IsStale(shownEpoch))
            {
                return;
            }

            var id = await Notify(NotificationText.EscapeIfMarkup(body, await BodyMarkup()));
            var old = Remember(id, address, version, shownEpoch);
            await Task.WhenAll(old.Select(CloseQuietly));
        }
        catch (Exception exception)
        {
            if (!IsDisposed())
            {
                logger.LogWarning(exception, "The system could not show a desktop notification");
            }
        }
    }

    private Task EnsureObserving()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return observing ??= Observe();
        }
    }

    private async Task Observe()
    {
        await connection.ConnectAsync();
        var selected = await connection.WatchSignalAsync<(uint Id, string Action)>(
            Destination,
            Path,
            Interface,
            "ActionInvoked",
            ReadAction,
            ActionInvoked,
            this,
            emitOnCapturedContext: false,
            ObserverFlags.EmitOnConnectionDispose);
        try
        {
            var closed = await connection.WatchSignalAsync<(uint Id, uint Reason)>(
                Destination,
                Path,
                Interface,
                "NotificationClosed",
                ReadClose,
                NotificationClosed,
                this,
                emitOnCapturedContext: false,
                ObserverFlags.EmitOnConnectionDispose);
            var keep = false;
            lock (gate)
            {
                if (!disposed)
                {
                    actionObserver = selected;
                    closedObserver = closed;
                    keep = true;
                }
            }

            if (!keep)
            {
                selected.Dispose();
                closed.Dispose();
            }
        }
        catch
        {
            selected.Dispose();
            throw;
        }
    }

    // Read once. A server that does not answer is treated as markup, so a name cannot become a link.
    private async Task<bool> BodyMarkup()
    {
        if (bodyMarkup is { } known)
        {
            return known;
        }

        var markup = true;
        try
        {
            var capabilities = await connection.CallMethodAsync(
                CapabilitiesRequest(),
                static (message, _) => message.GetBodyReader().ReadArrayOfString(),
                this);
            markup = NotificationText.UsesBodyMarkup(capabilities);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "The notification server did not say whether it reads markup, so the body is escaped");
        }

        bodyMarkup = markup;
        return markup;
    }

    // The writer cannot live across the await, so the request is built before the call.
    private MessageBuffer CapabilitiesRequest()
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Destination, Path, Interface, "GetCapabilities", "");
        return writer.CreateMessage();
    }

    private Task<uint> Notify(string body)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Destination, Path, Interface, "Notify", "susssasa{sv}i");
        writer.WriteString("Drover");
        writer.WriteUInt32(0);
        writer.WriteString(string.Empty);
        writer.WriteString("Drover");
        writer.WriteString(body);
        writer.WriteArray(actions);
        writer.WriteDictionary(hints);
        writer.WriteInt32(-1);
        return connection.CallMethodAsync(
            writer.CreateMessage(),
            static (message, state) => message.GetBodyReader().ReadUInt32(),
            this);
    }

    private Task Close(uint id)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Destination, Path, Interface, "CloseNotification", "u");
        writer.WriteUInt32(id);
        return connection.CallMethodAsync(writer.CreateMessage());
    }

    private async Task CloseQuietly(uint id)
    {
        try
        {
            await Close(id);
        }
        catch (Exception exception)
        {
            if (!IsDisposed())
            {
                logger.LogDebug(exception, "The system could not dismiss a desktop notification");
            }
        }
    }

    private uint[] Remember(uint id, string address, long version, long shownEpoch)
    {
        lock (gate)
        {
            if (disposed || shownEpoch != epoch)
            {
                return [id];
            }

            destinations[id] = new(id, address, version, Interlocked.Increment(ref sequence));
            var old = destinations.Values
                .OrderBy(value => value.Sequence)
                .Take(Math.Max(0, destinations.Count - 32))
                .Select(value => value.Id)
                .ToArray();
            foreach (var oldId in old)
            {
                destinations.Remove(oldId);
            }

            return old;
        }
    }

    private LinuxDestination? Forget(uint id)
    {
        lock (gate)
        {
            return destinations.Remove(id, out var destination) ? destination : null;
        }
    }

    private void ActionInvoked(Exception? exception, (uint Id, string Action) signal)
    {
        if (exception is not null)
        {
            SignalFailed(exception);
            return;
        }

        if (signal.Action == DefaultAction && Forget(signal.Id) is { } destination)
        {
            Track(() => CloseQuietly(signal.Id));
            Open(destination);
        }
    }

    private void NotificationClosed(Exception? exception, (uint Id, uint Reason) signal)
    {
        if (exception is not null)
        {
            SignalFailed(exception);
            return;
        }

        Forget(signal.Id);
    }

    private void SignalFailed(Exception exception)
    {
        if (!IsDisposed())
        {
            logger.LogWarning(exception, "The desktop notification session bus disconnected");
        }
    }

    /// <summary>
    /// Whether the notifications were cleared, or the connection began closing, since this one was asked for.
    /// </summary>
    private bool IsStale(long shownEpoch)
    {
        lock (gate)
        {
            return disposed || shownEpoch != epoch;
        }
    }

    private bool IsDisposed()
    {
        lock (gate)
        {
            return disposed;
        }
    }

    private void Open(LinuxDestination destination)
    {
        lock (gate)
        {
            if (!disposed)
            {
                open(destination.Address, destination.Version);
            }
        }
    }

    private async Task ShutDown(
        Task[] operations,
        uint[] ids,
        IDisposable? selected,
        IDisposable? closed)
    {
        try
        {
            await Task.WhenAll([.. operations, .. ids.Select(CloseQuietly)]).WaitAsync(shutdownLimit);
        }
        catch (TimeoutException exception)
        {
            logger.LogDebug(exception, "Timed out while dismissing desktop notifications");
        }
        finally
        {
            selected?.Dispose();
            closed?.Dispose();
            connection.Dispose();
        }
    }

    private static (uint Id, string Action) ReadAction(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        return (reader.ReadUInt32(), reader.ReadString());
    }

    private static (uint Id, uint Reason) ReadClose(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        return (reader.ReadUInt32(), reader.ReadUInt32());
    }

    #endregion

    #region Native Types

    private sealed record LinuxDestination(
        uint Id,
        string Address,
        long Version,
        long Sequence);

    #endregion
}
