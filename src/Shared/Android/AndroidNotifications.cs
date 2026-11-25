namespace Drover.App.Platforms.Android;

internal static class AndroidNotifications

{
    #region Private Fields

    private const string Channel = "drover-runs";
    private const string ContentKey = "drover.conversation.content";
    private const int NoticeId = 1;
    private const int SummaryId = 2;
    private const string LockScreenPreview = "drover.push.lock-screen-preview";
    private static readonly object gate = new();

    internal const string ReplyText = "drover.reply.text";
    internal const string OpenAction = "com.forresttechnologies.drover.OPEN_CONVERSATION";
    internal const string ReplyAction = "com.forresttechnologies.drover.REPLY";
    internal const string DismissAction = "com.forresttechnologies.drover.DISMISS_CONVERSATION";

    #endregion

    #region Properties

    private static Context Context => AndroidApplication.Context;

    private static NotificationManager Manager =>
        (NotificationManager)Context.GetSystemService(global::Android.Content.Context.NotificationService)!;

    public static bool Allowed => Manager.AreNotificationsEnabled()
        && (!OperatingSystem.IsAndroidVersionAtLeast(26)
            || Manager.GetNotificationChannel(Channel)?.Importance != NotificationImportance.None);

    public static bool ShowMessageTextOnLockScreen => Preferences.Default.Get(LockScreenPreview, false);

    #endregion

    #region Public Methods

    public static void SetShowMessageTextOnLockScreen(bool show)
    {
        lock (gate)
        {
            Preferences.Default.Set(LockScreenPreview, show);
            foreach (var notice in Manager.GetActiveNotifications() ?? [])
            {
                if (notice.Id == NoticeId && notice.Tag is { } key && notice.Notification is { } notification
                    && ReadContent(notification) is { } content)
                {
                    using var builder = Notification.Builder.RecoverBuilder(Context, notification)!;
                    ApplyPrivacy(builder, content);
                    using var updated = builder.SetOnlyAlertOnce(true)!.Build();
                    Manager.Notify(key, NoticeId, updated);
                }
            }
        }
    }

    public static void Show(PushWakeText wake, AndroidPushSubscription saved, bool canReply)
    {
        if (!Allowed)
        {
            return;
        }

        var target = new ConversationTarget(
            wake.BotId, wake.ThreadId, wake.IsMain, saved.UserId, saved.Token, wake.SpaceId);
        lock (gate)
        {
            var bot = wake.BotName ?? "Drover";
            var title = wake.ConversationTitle ?? bot;
            var before = ReadContent(FindActive(target.Key));
            var content = (before ?? new AndroidNotificationContent(bot, title, [])) with
            {
                BotName = bot,
                Title = title,
                Status = wake.ReplyText is null ? wake.Body : null,
                CanReply = canReply,
            };
            if (wake.ReplyText is { } text)
            {
                var sentAt = (wake.ReplyCreatedAt ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();
                content = content.Add(new(text, sentAt, FromOwner: false, wake.ReplyMessageId));
            }

            Manager.Cancel(target.Thread.ToString(), NoticeId);
            PostConversation(target, content, quiet: false);
        }
    }

    public static void ShowReplyResult(ConversationTarget target, string draft, MessageResponse? sent)
    {
        lock (gate)
        {
            var content = ReadContent(FindActive(target.Key));
            if (content is null)
            {
                var legacyTag = target.Thread.ToString();
                if (FindActive(legacyTag) is null)
                {
                    return;
                }

                Manager.Cancel(legacyTag, NoticeId);
                content = new AndroidNotificationContent("Drover", "Drover", [], CanReply: true);
            }

            content = sent is null
                ? content with { Draft = draft }
                : content.Add(new(draft.Trim(), sent.CreatedAt.ToUnixTimeMilliseconds(), FromOwner: true, sent.Id))
                    with { Draft = null };
            PostConversation(target, content, quiet: true);
        }
    }

    public static void ShowChange() => PostGeneric("Something changed. Open Drover to see it.");

    public static void ShowTest() => PostGeneric("Drover can send notifications to this device.");

    public static void Clear()
    {
        lock (gate)
        {
            Manager.CancelAll();
        }
    }

    public static void Dismiss(ConversationTarget target)
    {
        lock (gate)
        {
            Manager.Cancel(target.Key, NoticeId);
            Manager.Cancel(target.Thread.ToString(), NoticeId);
            UpdateGroup(target.Group, target.Key, includesNew: false);
        }
    }

    public static void Dismissed(Intent intent)
    {
        if (intent.GetStringExtra("group") is { } group && intent.GetStringExtra("key") is { } key)
        {
            lock (gate)
            {
                UpdateGroup(group, key, includesNew: false);
            }
        }
    }

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

    internal static ConversationTarget? ReadTarget(Intent intent)
    {
        if (!Guid.TryParse(intent.GetStringExtra("bot"), out var bot) || bot == Guid.Empty
            || !Guid.TryParse(intent.GetStringExtra("thread"), out var thread) || thread == Guid.Empty
            || !Guid.TryParse(intent.GetStringExtra("user"), out var user)
            || intent.GetStringExtra("registration") is not { Length: > 0 } registration)
        {
            return null;
        }

        Guid? space = Guid.TryParse(intent.GetStringExtra("space"), out var parsed) ? parsed : null;
        return new(bot, thread, intent.GetBooleanExtra("main", true), user, registration, space);
    }

    #endregion

    #region Private Methods

    private static void PostConversation(ConversationTarget target, AndroidNotificationContent content, bool quiet)
    {
        if (!Allowed)
        {
            return;
        }

        using var openIntent = PutTarget(
            new Intent(Context, typeof(MainActivity)), OpenAction, "conversation", target);
        using var open = PendingIntent.GetActivity(
            Context, 0, openIntent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        using var dismissIntent = new Intent(Context, typeof(NotificationReplyReceiver)).SetAction(DismissAction)!
            .SetData(AndroidUri.Parse($"drover://dismiss/{target.Key}"))!
            .PutExtra("group", target.Group)!.PutExtra("key", target.Key);
        using var dismiss = PendingIntent.GetBroadcast(
            Context, 0, dismissIntent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        using var builder = Build();
        using var extras = new Bundle();
        extras.PutString(ContentKey,
            JsonSerializer.Serialize(content, AndroidNotificationJson.Default.AndroidNotificationContent));
        builder.AddExtras(extras)!.SetContentIntent(open)!.SetDeleteIntent(dismiss)!
            .SetOnlyAlertOnce(quiet)!.SetGroup(UpdateGroup(target.Group, target.Key, includesNew: true));
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            builder.SetGroupAlertBehavior(NotificationGroupAlertBehavior.Children);
        }

        Fill(builder, content);
        ApplyPrivacy(builder, content);
        if (content.CanReply && target.Space is not null && OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            using var reply = ReplyFor(target);
            builder.AddAction(reply);
        }

        using var notification = builder.Build();
        Manager.Notify(target.Key, NoticeId, notification);
    }

    private static string? UpdateGroup(string group, string changedKey, bool includesNew)
    {
        var others = (Manager.GetActiveNotifications() ?? [])
            .Where(notice => notice.Id == NoticeId && notice.Tag is { } key && key != changedKey
                && key.StartsWith($"{group}/", StringComparison.Ordinal))
            .ToArray();
        var grouped = others.Length + (includesNew ? 1 : 0) > 1;
        var groupKey = grouped ? group : null;
        if (!grouped)
        {
            // Cancel the summary before reposting a survivor because Android cancels its grouped children too.
            Manager.Cancel(group, SummaryId);
        }

        foreach (var notice in others)
        {
            if (notice.Notification is { } notification && notification.Group != groupKey)
            {
                using var builder = Notification.Builder.RecoverBuilder(Context, notification)!;
                using var updated = builder.SetGroup(groupKey)!.SetOnlyAlertOnce(true)!.Build();
                Manager.Notify(notice.Tag, NoticeId, updated);
            }
        }

        if (grouped)
        {
            using var summary = Build().SetContentTitle("Drover")!.SetContentText("Conversation updates")!
                .SetVisibility(NotificationVisibility.Private)!.SetGroup(group)!.SetGroupSummary(true)!
                .SetOnlyAlertOnce(true)!;
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                summary.SetGroupAlertBehavior(NotificationGroupAlertBehavior.Children);
            }
            else
            {
                summary.SetDefaults(0);
            }

            using var notification = summary.Build();
            Manager.Notify(group, SummaryId, notification);
        }

        return groupKey;
    }

    private static Notification? FindActive(string tag) =>
        (Manager.GetActiveNotifications() ?? []).FirstOrDefault(
            notice => notice.Id == NoticeId && notice.Tag == tag)?.Notification;

    private static AndroidNotificationContent? ReadContent(Notification? notification) =>
        notification?.Extras?.GetString(ContentKey) is { } stored
            ? JsonSerializer.Deserialize(stored, AndroidNotificationJson.Default.AndroidNotificationContent)
            : null;

    private static void Fill(Notification.Builder builder, AndroidNotificationContent content)
    {
        var failure = content.Draft is { } draft
            ? $"Could not confirm your reply. Open Drover to check the conversation.\n{draft}"
            : null;
        var body = failure ?? content.Status ?? content.Messages.LastOrDefault()?.Text ?? "New reply";
        builder.SetContentTitle(content.Title)!.SetContentText(body);
        if (OperatingSystem.IsAndroidVersionAtLeast(24) && content.Messages.Length > 0)
        {
            AddConversationStyle(builder, content, failure);
        }
        else
        {
            using var style = new Notification.BigTextStyle().BigText(body);
            builder.SetStyle(style);
        }
    }

    [SupportedOSPlatform("android24.0")]
    private static void AddConversationStyle(
        Notification.Builder builder, AndroidNotificationContent content, string? failure)
    {
        using var style = StartConversationStyle(content.Title);
        foreach (var message in content.Messages)
        {
            AddMessage(style, message.Text, message.SentAt, message.FromOwner ? null : content.BotName);
        }

        if (content.Status is { } status)
        {
            AddMessage(style, status, Now(), "Drover");
        }

        if (failure is not null)
        {
            AddMessage(style, failure, Now(), "Drover");
        }

        builder.SetStyle(style);
    }

    [SupportedOSPlatform("android24.0")]
    private static Notification.MessagingStyle StartConversationStyle(string title)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            using var owner = new Person.Builder().SetName("You")!.Build()!;
            var style = new Notification.MessagingStyle(owner);
            style.SetConversationTitle(title)!.SetGroupConversation(true);
            return style;
        }

        // Android 7 and 8 use the name-based constructor.
        var legacy = new Notification.MessagingStyle("You");
        legacy.SetConversationTitle(title);
        return legacy;
    }

    [SupportedOSPlatform("android24.0")]
    private static void AddMessage(Notification.MessagingStyle style, string text, long timestamp, string? sender)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            using var person = sender is null ? null : new Person.Builder().SetName(sender)!.Build();
            style.AddMessage(text, timestamp, person);
        }
        else
        {
            style.AddMessage(text, timestamp, sender);
        }
    }

    private static void ApplyPrivacy(Notification.Builder builder, AndroidNotificationContent content)
    {
        if (content.PublicVersion(ShowMessageTextOnLockScreen) is { } preview)
        {
            using var redacted = Build().SetContentTitle(preview.Title)!.SetContentText(preview.Body)!;
            using var publicVersion = redacted.Build();
            builder.SetVisibility(NotificationVisibility.Private)!.SetPublicVersion(publicVersion);
        }
        else
        {
            builder.SetVisibility(NotificationVisibility.Public)!.SetPublicVersion(null);
        }
    }

    private static void PostGeneric(string body)
    {
        if (!Allowed)
        {
            return;
        }

        using var intent = new Intent(Context, typeof(MainActivity)).SetAction(OpenAction);
        using var open = PendingIntent.GetActivity(Context, 0, intent, PendingIntentFlags.Immutable);
        using var builder = Build().SetContentTitle("Drover")!.SetContentText(body)!
            .SetVisibility(NotificationVisibility.Private)!.SetContentIntent(open)!.SetAutoCancel(true)!;
        using var notification = builder.Build();
        Manager.Notify("drover-device", NoticeId, notification);
    }

    private static Notification.Builder Build()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return NewNotice(new Notification.Builder(Context));
        }

        using var channel = new NotificationChannel(Channel, "Bot updates", NotificationImportance.Default)
        {
            Description = "Bot replies, failed runs, and requests that need your attention",
        };
        Manager.CreateNotificationChannel(channel);
        return NewNotice(new Notification.Builder(Context, Channel));
    }

    private static Notification.Builder NewNotice(Notification.Builder builder) =>
        builder.SetSmallIcon(Resource.Drawable.drover_notification)!.SetCategory(Notification.CategoryMessage)!;

    private static Intent PutTarget(Intent intent, string action, string host, ConversationTarget target) =>
        intent.SetAction(action)!.SetData(AndroidUri.Parse($"drover://{host}/{target.Key}"))!
            .PutExtra("bot", target.Bot.ToString())!.PutExtra("thread", target.Thread.ToString())!
            .PutExtra("space", target.Space?.ToString())!.PutExtra("user", target.User.ToString())!
            .PutExtra("registration", target.Registration)!.PutExtra("main", target.Main)!;

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [SupportedOSPlatform("android31.0")]
    private static Notification.Action? ReplyFor(ConversationTarget conversation)
    {
        using var intent = PutTarget(
            new Intent(Context, typeof(NotificationReplyReceiver)), ReplyAction, "reply", conversation);
        using var target = PendingIntent.GetBroadcast(
            Context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable);
        using var input = new RemoteInput.Builder(ReplyText).SetLabel("Reply to bot")!.Build();
        using var action = new Notification.Action.Builder(global::Android.Resource.Drawable.IcMenuSend, "Reply", target);
        return action.AddRemoteInput(input)!.SetAllowGeneratedReplies(false)!.SetAuthenticationRequired(true).Build();
    }

    #endregion
}
