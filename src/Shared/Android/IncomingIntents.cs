namespace Drover.App.Platforms.Android;

/// <summary>
/// What Android hands the app's activity from outside it: a tapped notification, a browser coming back from a tool
/// server's sign-in page, or something another app shared. Each becomes an address for the interface to open, or a
/// share for it to place. The activity calls this when it is created and for each new intent.
/// </summary>
internal static class IncomingIntents
{
    #region Private Fields

    /// <summary>The host of the address a browser comes back into the app with after a tool server's sign-in page.</summary>
    public const string SignInReturnHost = "tool-sign-in";

    // A ticket is 32 random bytes in URL-safe base64. Anything else is not one of this server's.
    private const int TicketLength = 43;

    #endregion

    #region Public Methods

    /// <param name="intent">The intent the activity was given.</param>
    /// <param name="signInReturnScheme">This app's own scheme, its package name, which the server sends a browser back to.</param>
    public static void Take(Intent? intent, string signInReturnScheme)
    {
        OpenConversation(intent);
        ReturnFromSignIn(intent, signInReturnScheme);
        TakeShare(intent);
    }

    #endregion

    #region Private Methods

    // The activity is open to every app, so any of them can send this. A notification this app posted carries the
    // person and the registration it was made for; an intent without them, or with ones that are not this device's
    // current ones, goes nowhere.
    private static void OpenConversation(Intent? intent)
    {
        if (intent?.Action != AndroidNotifications.OpenAction
            || !AndroidNotifications.Conversation(intent, out var bot, out var thread)
            || !Guid.TryParse(intent.GetStringExtra("user"), out var user)
            || intent.GetStringExtra("registration") is not { Length: > 0 } registration)
        {
            return;
        }

        var address = intent.GetBooleanExtra("main", true) ? Addresses.Talk(bot) : Addresses.SideChat(bot, thread);
        Guid? space = Guid.TryParse(intent.GetStringExtra("space"), out var named) ? named : null;
        _ = OpenIfOurs(user, registration, address, space);
    }

    private static async Task OpenIfOurs(Guid user, string registration, string address, Guid? space)
    {
        var services = IPlatformApplication.Current?.Services;
        if (services?.GetService<AndroidPushRegistration>() is { } registrations
            && await registrations.BelongsTo(user, registration))
        {
            services.GetService<IncomingNavigation>()?.Open(address, space);
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
    private static void ReturnFromSignIn(Intent? intent, string scheme)
    {
        if (intent is { Action: Intent.ActionView, Data: { Host: SignInReturnHost } address }
            && address.Scheme == scheme
            && address.GetQueryParameter("ticket") is { Length: TicketLength } ticket
            && ticket.All(letter => char.IsAsciiLetterOrDigit(letter) || letter is '-' or '_'))
        {
            IPlatformApplication.Current?.Services.GetService<IncomingNavigation>()?.Open(Addresses.ToolSignInReturn(ticket));
        }
    }

    #endregion
}
