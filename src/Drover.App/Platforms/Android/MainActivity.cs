namespace Drover.App.Platforms.Android;

[global::Android.App.Activity(
    Theme = "@style/Maui.MainTheme.NoActionBar",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = SignInReturnScheme,
    DataHost = IncomingIntents.SignInReturnHost)]
[IntentFilter([Intent.ActionSend, Intent.ActionSendMultiple], Categories = [Intent.CategoryDefault], DataMimeType = "*/*")]
public sealed class MainActivity : MauiAppCompatActivity
{
    #region Private Fields

    /// <summary>
    /// The scheme of the address a browser comes back into this app with after a tool server's sign-in page. It is
    /// the package name, and the server sends a browser only to the schemes of Drover's own apps.
    /// </summary>
    public const string SignInReturnScheme = "com.forresttechnologies.drover.preview";

    #endregion

    #region Private Methods

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // The launch intent stays with the activity. It is acted on once: not when Android rebuilds the activity,
        // and not when the app is opened again from the recent apps.
        if (savedInstanceState is null && Intent?.Flags.HasFlag(ActivityFlags.LaunchedFromHistory) != true)
        {
            IncomingIntents.Take(Intent, SignInReturnScheme);
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

    #endregion
}
