namespace Drover.App.Platforms.Android;

internal static class AndroidNotifications
{
    #region Private Fields

    private const string Channel = "drover-runs";

    internal const string ReplyText = "drover.reply.text";

    internal const string OpenAction = "com.forresttechnologies.drover.OPEN_CONVERSATION";

    internal const string ReplyAction = "com.forresttechnologies.drover.REPLY";

    #endregion

    #region Properties

    private static NotificationManager Manager =>
        (NotificationManager)AndroidApplication.Context.GetSystemService(Context.NotificationService)!;

    public static bool Allowed => Manager.AreNotificationsEnabled()
        && (!OperatingSystem.IsAndroidVersionAtLeast(26) || Manager.GetNotificationChannel(Channel)?.Importance != NotificationImportance.None);

    #endregion

    #region Public Methods

    public static void Show(PushWakeText wake, AndroidPushSubscription saved, bool canReply)
    {
        if (Allowed)
        {
            // A reply is offered only where Android can ask for the device to be unlocked first. Without that,
            // whoever holds a locked phone could write to a bot as its owner.
            Post(wake.BotId, wake.ThreadId, wake.IsMain, wake.Body,
                canReply && wake.SpaceId is { } space && OperatingSystem.IsAndroidVersionAtLeast(31) ? ReplyFor(wake, saved, space) : null);
        }
    }

    public static void ShowReplyResult(Guid bot, Guid thread, bool main, string body) =>
        Post(bot, thread, main, body);

    /// <summary>For a wake-up whose words could not be fetched: the person still learns that something happened.</summary>
    public static void ShowChange() => Post(Guid.Empty, Guid.Empty, true, "Something changed. Open Drover to see it.");

    /// <summary>Takes a conversation's notification away, which also ends a reply Android still shows as sending.</summary>
    public static void Clear(Guid thread) => Manager.Cancel(thread.ToString(), 1);

    public static void ShowTest() => Post(Guid.Empty, Guid.Empty, true, "Drover can send notifications to this device.");

    public static void Clear() => Manager.CancelAll();

    public static void OpenSettings() =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Platform.CurrentActivity is not { } activity)
            {
                return;
            }

            using var intent = OperatingSystem.IsAndroidVersionAtLeast(26)
                ? new Intent(global::Android.Provider.Settings.ActionAppNotificationSettings)
                    .PutExtra(global::Android.Provider.Settings.ExtraAppPackage, activity.PackageName)
                : new Intent(global::Android.Provider.Settings.ActionApplicationDetailsSettings,
                    AndroidUri.Parse($"package:{activity.PackageName}"));
            activity.StartActivity(intent);
        });

    internal static bool Conversation(Intent intent, out Guid bot, out Guid thread)
    {
        var botKnown = Guid.TryParse(intent.GetStringExtra("bot"), out bot);
        var threadKnown = Guid.TryParse(intent.GetStringExtra("thread"), out thread);
        return botKnown && threadKnown && bot != Guid.Empty && thread != Guid.Empty;
    }

    #endregion

    #region Private Methods

    private static void Post(Guid bot, Guid thread, bool main, string body, Notification.Action? reply = null)
    {
        if (!Allowed)
        {
            return;
        }

        var manager = Manager;
        var context = AndroidApplication.Context;
        using var builder = Build(context, manager);
        using var target = new Intent(context, typeof(MainActivity)).SetAction(OpenAction)!
            .SetData(AndroidUri.Parse($"drover://conversation/{thread}"))!
            .PutExtra("bot", bot.ToString())!.PutExtra("thread", thread.ToString())!.PutExtra("main", main)!;
        using var open = PendingIntent.GetActivity(context, 0, target, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        builder.SetSmallIcon(Resource.Drawable.drover_notification)!
            .SetContentTitle("Drover")!
            .SetContentText(body)!
            .SetVisibility(NotificationVisibility.Private)!
            .SetCategory(Notification.CategoryMessage)!
            .SetContentIntent(open)!
            .SetAutoCancel(true);
        if (reply is not null)
        {
            builder.AddAction(reply);
        }

        using var notification = builder.Build();
        manager.Notify(thread.ToString(), 1, notification);
    }

    private static Notification.Builder Build(Context context, NotificationManager manager)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return new Notification.Builder(context);
        }

        using var channel = new NotificationChannel(Channel, "Bot updates", NotificationImportance.Default)
        {
            Description = "Bot replies, failed runs, and requests that need your attention",
        };
        manager.CreateNotificationChannel(channel);
        return new Notification.Builder(context, Channel);
    }

    [SupportedOSPlatform("android31.0")]
    private static Notification.Action? ReplyFor(PushWakeText wake, AndroidPushSubscription saved, Guid space)
    {
        var context = AndroidApplication.Context;
        using var intent = new Intent(context, typeof(NotificationReplyReceiver)).SetAction(ReplyAction)!
            .SetData(AndroidUri.Parse($"drover://reply/{wake.ThreadId}"))!
            .PutExtra("bot", wake.BotId.ToString())!.PutExtra("thread", wake.ThreadId.ToString())!
            .PutExtra("space", space.ToString())!.PutExtra("user", saved.UserId.ToString())!
            .PutExtra("registration", saved.Token)!.PutExtra("main", wake.IsMain)!;
        using var target = PendingIntent.GetBroadcast(context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable);
        using var input = new RemoteInput.Builder(ReplyText).SetLabel("Reply to bot")!.Build();
        using var action = new Notification.Action.Builder(global::Android.Resource.Drawable.IcMenuSend, "Reply", target);
        return action.AddRemoteInput(input)!.SetAllowGeneratedReplies(false)!.SetAuthenticationRequired(true).Build();
    }

    #endregion
}
