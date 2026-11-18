namespace Drover.Native.Droid;

// The Android code shared with the Android shell (src/Shared/Android) asks its host for a few things through the
// names that shell's framework gives them. These are those names, with the same shapes, over Android and the
// platform this app is built on, so the same source compiles in both apps. Their names cannot change here without
// forking that code, so the Async suffix the shell's framework uses stays.
#pragma warning disable DRV0001

/// <summary>Runs work on Android's main thread, where the interface and the platform's dialogs live.</summary>
public static class MainThread
{
    #region Private Fields

    private static readonly Android.OS.Handler handler = new(Android.OS.Looper.MainLooper!);

    #endregion

    #region Properties

    public static bool IsMainThread => Android.OS.Looper.MyLooper() == Android.OS.Looper.MainLooper;

    #endregion

    #region Public Methods

    public static void BeginInvokeOnMainThread(Action action)
    {
        if (IsMainThread)
        {
            action();
        }
        else
        {
            handler.Post(action);
        }
    }

    public static Task InvokeOnMainThreadAsync(Action action) => InvokeOnMainThreadAsync(() =>
    {
        action();
        return true;
    });

    public static Task<T> InvokeOnMainThreadAsync<T>(Func<T> call)
    {
        if (IsMainThread)
        {
            return Run(call);
        }

        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Post(() => Run(call).ContinueWith(done => result.SetFromTask(done), TaskScheduler.Default));
        return result.Task;
    }

    public static Task InvokeOnMainThreadAsync(Func<Task> call) => InvokeOnMainThreadAsync<Task>(call).Unwrap();

    public static Task<T> InvokeOnMainThreadAsync<T>(Func<Task<T>> call) => InvokeOnMainThreadAsync<Task<T>>(call).Unwrap();

    #endregion

    #region Private Methods

    // A failure is carried in the task, as an awaited call would carry it.
    private static Task<T> Run<T>(Func<T> call)
    {
        try
        {
            return Task.FromResult(call());
        }
        catch (Exception exception)
        {
            return Task.FromException<T>(exception);
        }
    }

    #endregion
}

/// <summary>The activity the person sees, if there is one: the platform's single activity.</summary>
public static class Platform
{
    public static Activity? CurrentActivity => Uno.UI.ContextHelper.Current as Activity;
}

/// <summary>The result of asking for a permission.</summary>
public enum PermissionStatus
{
    Unknown,
    Denied,
    Granted,
}

/// <summary>Asks Android for the one permission the app asks for at run time: to show notifications.</summary>
public static class Permissions
{
    #region Private Fields

    private const int NotificationsRequest = 16902;

    private static TaskCompletionSource<PermissionStatus>? waiting;

    #endregion

    #region Public Methods

    public static Task<PermissionStatus> RequestAsync<TPermission>()
        where TPermission : PostNotifications
    {
        var context = AndroidApplication.Context;
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)
            || context.CheckSelfPermission(Android.Manifest.Permission.PostNotifications) == Permission.Granted)
        {
            return Task.FromResult(PermissionStatus.Granted);
        }

        if (Platform.CurrentActivity is not { } activity)
        {
            return Task.FromResult(PermissionStatus.Unknown);
        }

        waiting?.TrySetResult(PermissionStatus.Unknown);
        waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RequestPermissions([Android.Manifest.Permission.PostNotifications], NotificationsRequest);
        return waiting.Task;
    }

    /// <summary>The activity passes on Android's answer to every permission request.</summary>
    public static void Complete(int request, Permission[] results)
    {
        if (request == NotificationsRequest)
        {
            waiting?.TrySetResult(results is [Permission.Granted] ? PermissionStatus.Granted : PermissionStatus.Denied);
            waiting = null;
        }
    }

    #endregion

    #region Nested Types

    /// <summary>The permission to show notifications.</summary>
    public class PostNotifications;

    #endregion
}

/// <summary>The app's own folders on the device.</summary>
public static class FileSystem
{
    public static string CacheDirectory => AndroidApplication.Context.CacheDir!.AbsolutePath;
}

/// <summary>
/// The running app's services. A broadcast (a notification arriving) can start the process with no activity and so
/// with no interface; then the services are made for that work alone, and the interface makes its own when it starts.
/// </summary>
public interface IPlatformApplication
{
    #region Properties

    // The interface has its services once it has a window: it makes them as it shows the first screen.
    static IPlatformApplication? Current => Microsoft.UI.Xaml.Application.Current is App { MainWindow: not null } app
        ? new Running(app.Services)
        : new Running(ColdStart.Services);

    IServiceProvider Services { get; }

    #endregion

    #region Nested Types

    private sealed record Running(IServiceProvider Services) : IPlatformApplication;

    private static class ColdStart
    {
        public static IServiceProvider Services { get; } = AppServices.Build();
    }

    #endregion
}

#pragma warning restore DRV0001
