namespace Drover.App.Platforms.Android;

[global::Android.App.Activity(
    Theme = "@style/Maui.MainTheme.NoActionBar",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
    #region Private Methods

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        OpenConversation(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        OpenConversation(intent);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        IPlatformApplication.Current?.Services.GetService<DocumentDestination>()?.Complete(requestCode, resultCode, data);
    }

    private static void OpenConversation(Intent? intent)
    {
        if (intent?.Action == AndroidNotifications.OpenAction && AndroidNotifications.Conversation(intent, out var bot, out var thread))
        {
            var address = intent.GetBooleanExtra("main", true) ? Addresses.Talk(bot) : Addresses.SideChat(bot, thread);
            IPlatformApplication.Current?.Services.GetService<NotificationNavigation>()?.Open(address);
        }
    }

    #endregion
}
