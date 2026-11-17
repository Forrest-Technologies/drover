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
    DataHost = SignInReturnHost)]
[IntentFilter([Intent.ActionSend, Intent.ActionSendMultiple], Categories = [Intent.CategoryDefault], DataMimeType = "*/*")]
public sealed class MainActivity : MauiAppCompatActivity
{
    #region Private Fields

    /// <summary>
    /// The scheme of the address a browser comes back into this app with after a tool server's sign-in page. It is
    /// the package name, and the server sends a browser only to the schemes of Drover's own apps.
    /// </summary>
    public const string SignInReturnScheme = "com.forresttechnologies.drover.preview";

    private const string SignInReturnHost = "tool-sign-in";

    // A ticket is 32 random bytes in URL-safe base64. Anything else is not one of this server's.
    private const int TicketLength = 43;

    #endregion

    #region Private Methods

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // The launch intent stays with the activity. It is acted on once: not when Android rebuilds the activity,
        // and not when the app is opened again from the recent apps.
        if (savedInstanceState is null && Intent?.Flags.HasFlag(ActivityFlags.LaunchedFromHistory) != true)
        {
            OpenConversation(Intent);
            ReturnFromSignIn(Intent);
            TakeShare(Intent);
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        OpenConversation(intent);
        ReturnFromSignIn(intent);
        TakeShare(intent);
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
            IPlatformApplication.Current?.Services.GetService<IncomingNavigation>()?.Open(address);
        }
    }

    private static void TakeShare(Intent? intent)
    {
        if (AndroidShares.IsShare(intent) && IPlatformApplication.Current?.Services.GetService<AndroidShares>() is { } shares)
        {
            _ = shares.Receive(intent!);
        }
    }

    // Any app or page can send this address. The ticket means something only to the server, and only for the
    // signed-in person who started the sign-in, so all that is done here is to hand it to the tools screen.
    private static void ReturnFromSignIn(Intent? intent)
    {
        if (intent is { Action: Intent.ActionView, Data: { Scheme: SignInReturnScheme, Host: SignInReturnHost } address }
            && address.GetQueryParameter("ticket") is { Length: TicketLength } ticket
            && ticket.All(letter => char.IsAsciiLetterOrDigit(letter) || letter is '-' or '_'))
        {
            IPlatformApplication.Current?.Services.GetService<IncomingNavigation>()?.Open(Addresses.ToolSignInReturn(ticket));
        }
    }

    #endregion
}
