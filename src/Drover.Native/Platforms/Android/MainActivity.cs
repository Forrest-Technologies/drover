namespace Drover.Native.Droid;

/// <summary>
/// The one screen Android knows about. Everything a person sees is drawn inside it by the shared app. It also takes
/// what Android hands the app from outside: a tapped notification, a browser coming back from a tool server's
/// sign-in page, and what another app shares.
/// </summary>
[Android.App.Activity(
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = Uno.UI.ActivityHelper.AllConfigChanges,
    WindowSoftInputMode = Android.Views.SoftInput.AdjustNothing | Android.Views.SoftInput.StateHidden,
    Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = SignInReturnScheme,
    DataHost = IncomingIntents.SignInReturnHost)]
[IntentFilter([Intent.ActionSend, Intent.ActionSendMultiple], Categories = [Intent.CategoryDefault], DataMimeType = "*/*")]
public sealed class MainActivity : Uno.UI.Runtime.Android.ApplicationActivity
{
    #region Private Fields

    /// <summary>
    /// The scheme of the address a browser comes back into this app with after a tool server's sign-in page. It is
    /// the package name, and the server sends a browser only to the schemes of Drover's own apps.
    /// </summary>
    public const string SignInReturnScheme = "com.forresttechnologies.drover.client";

    #endregion

    #region Public Methods

    public override bool DispatchKeyEvent(Android.Views.KeyEvent? e)
    {
        var editor = e?.Action is Android.Views.KeyEventActions.Down ? FocusedEditor() : null;
        var before = EditorText(editor);
        var handled = base.DispatchKeyEvent(e);

        // Uno handles these edits in XAML. Reopen the Android input connection with the
        // updated text so a following soft-keyboard edit cannot replay its old buffer.
        if (before is not null
            && ReferenceEquals(FocusedEditor(), editor)
            && before != EditorText(editor)
            && CurrentFocus is { } view
            && GetSystemService(InputMethodService) is Android.Views.InputMethods.InputMethodManager keyboard
            && keyboard.IsActive)
        {
            keyboard.RestartInput(view);
        }

        return handled;
    }

    internal void UpdateSystemBars(ShellView shell)
    {
        var light = shell.ActualTheme is ElementTheme.Light;
        var status = Windows.UI.ViewManagement.StatusBar.GetForCurrentView();
        // Uno returns the system's black or white foreground after mapping our theme color.
        if (status.ForegroundColor is not { } current || (current.R == 0) != light)
        {
            status.ForegroundColor = Tokens.Color(shell, "DvText");
        }

        if (Window is { } window)
        {
            using var controller = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
            controller?.AppearanceLightNavigationBars = light;
        }
    }

    #endregion

    #region Private Methods

    private static object? FocusedEditor() =>
        Microsoft.UI.Xaml.Application.Current is App { MainWindow.Content.XamlRoot: { } root }
            ? FocusManager.GetFocusedElement(root)
            : null;

    private static string? EditorText(object? editor) => editor switch
    {
        TextBox text => text.Text,
        PasswordBox password => password.Password,
        _ => null,
    };

    protected override void OnCreate(Bundle? bundle)
    {
#if DROVER_UNREACHABLE_CHECK
        if (Intent?.GetBooleanExtra("drover.unreachable_open", false) == true)
        {
            UnreachableOpenCheck.Arm();
        }
#endif

        AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);
        base.OnCreate(bundle);

        // The launch intent is acted on once: not when Android rebuilds the activity, and not when the app is opened
        // again from the recent apps.
        if (bundle is null && Intent?.Flags.HasFlag(ActivityFlags.LaunchedFromHistory) != true)
        {
            IncomingIntents.Take(Intent, SignInReturnScheme);
        }
    }

    protected override void OnStart()
    {
        base.OnStart();
        if (Microsoft.UI.Xaml.Application.Current is App { MainWindow.Content: ShellView shell })
        {
            UpdateSystemBars(shell);
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        IncomingIntents.Take(intent, SignInReturnScheme);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        IPlatformApplication.Current?.Services.GetService<DocumentDestination>()?.Complete(requestCode, resultCode, data);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        Permissions.Complete(requestCode, grantResults);
    }

    #endregion
}
