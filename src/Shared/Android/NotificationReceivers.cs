namespace Drover.App.Platforms.Android;

[BroadcastReceiver(Exported = true)]
[IntentFilter([UnifiedPushTransport.NewEndpoint, UnifiedPushTransport.Unregistered, UnifiedPushTransport.Message, UnifiedPushTransport.RegistrationFailed, UnifiedPushTransport.TemporarilyUnavailable])]
public sealed class UnifiedPushReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent is not null)
        {
            var sender = OperatingSystem.IsAndroidVersionAtLeast(34) ? SentFromPackage : null;
            _ = NotificationReceivers.Run(GoAsync(), (services, token) =>
                services.GetRequiredService<AndroidPushRegistration>().Receive(intent, sender, token));
        }
    }
}

[BroadcastReceiver(Exported = false)]
public sealed class NotificationReplyReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action == AndroidNotifications.DismissAction)
        {
            AndroidNotifications.Dismissed(intent);
        }
        else if (intent is not null)
        {
            _ = NotificationReceivers.Run(GoAsync(), (services, token) =>
                services.GetRequiredService<AndroidPushMessages>().Reply(intent, token));
        }
    }
}

internal static class NotificationReceivers
{
    public static async Task Run(BroadcastReceiver.PendingResult? result, Func<IServiceProvider, CancellationToken, Task> receive)
    {
        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var services = IPlatformApplication.Current?.Services;
        try
        {
            if (services is not null)
            {
                await using var scope = services.CreateAsyncScope();
                await receive(scope.ServiceProvider, patience.Token);
            }
        }
        catch (Exception)
        {
            services?.GetService<ILogger<UnifiedPushReceiver>>()?.LogWarning("Drover could not process a notification");
        }
        finally
        {
            result?.Finish();
        }
    }
}
