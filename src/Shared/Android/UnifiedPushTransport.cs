namespace Drover.App.Platforms.Android;

internal static class UnifiedPushTransport
{
    #region Private Fields

    public const string Message = "org.unifiedpush.android.connector.MESSAGE";

    public const string NewEndpoint = "org.unifiedpush.android.connector.NEW_ENDPOINT";

    public const string Unregistered = "org.unifiedpush.android.connector.UNREGISTERED";

    public const string TemporarilyUnavailable = "org.unifiedpush.android.connector.TEMP_UNAVAILABLE";

    public const string RegistrationFailed = "org.unifiedpush.android.connector.REGISTRATION_FAILED";

    public const string Register = "org.unifiedpush.android.distributor.REGISTER";

    public const string Unregister = "org.unifiedpush.android.distributor.UNREGISTER";

    private const string MessageAck = "org.unifiedpush.android.distributor.MESSAGE_ACK";

    #endregion

    #region Public Methods

    public static IReadOnlyList<(string Package, string Label)> Distributors()
    {
        var manager = AndroidApplication.Context.PackageManager!;
        using var intent = new Intent(Register);
        return [.. manager.QueryBroadcastReceivers(intent, 0)
            .Select(receiver => (receiver.ActivityInfo?.PackageName, receiver.LoadLabel(manager)))
            .Where(receiver => receiver.PackageName is not null)
            .Select(receiver => (receiver.PackageName!, receiver.Item2 ?? receiver.PackageName!))
            .DistinctBy(receiver => receiver.Item1)];
    }

    public static Task<string?> Choose() => MainThread.InvokeOnMainThreadAsync(() =>
    {
        var distributors = Distributors();
        return distributors.Count switch
        {
            0 => Task.FromResult<string?>(null),
            1 => Task.FromResult<string?>(distributors[0].Package),
            _ => Pick(distributors),
        };
    });

    public static void Send(AndroidPushSubscription saved, string action, string? detail = null)
    {
        var context = AndroidApplication.Context;
        using var intent = new Intent(action).SetPackage(saved.Distributor)!
            .PutExtra("token", saved.Token)!;
        if (action == Register)
        {
            using var identity = new Intent(context, typeof(UnifiedPushReceiver));
            using var pending = PendingIntent.GetBroadcast(context, 0, identity, PendingIntentFlags.Immutable);
            intent.PutExtra("pi", pending);
            intent.PutExtra("application", context.PackageName);
            intent.PutExtra("vapid", detail);
        }
        else if (action == MessageAck)
        {
            intent.PutExtra("id", detail);
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            using var options = BroadcastOptions.MakeBasic()!.SetShareIdentityEnabled(true);
            context.SendBroadcast(intent, null, options!.ToBundle());
        }
        else
        {
            context.SendBroadcast(intent);
        }
    }

    public static void Acknowledge(AndroidPushSubscription saved, Intent received)
    {
        if (received.GetStringExtra("id") is { Length: > 0 and <= 100 } id)
        {
            Send(saved, MessageAck, id);
        }
    }

    #endregion

    #region Private Methods

    private static Task<string?> Pick(IReadOnlyList<(string Package, string Label)> choices)
    {
        if (Platform.CurrentActivity is not { } activity)
        {
            return Task.FromResult<string?>(null);
        }

        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var prompt = new AlertDialog.Builder(activity);
        prompt.SetTitle("Choose a notification distributor");
        prompt.SetItems([.. choices.Select(choice => choice.Label)], (_, selection) => result.TrySetResult(choices[selection.Which].Package));
        prompt.SetOnCancelListener(new CancelChoice(result));
        prompt.SetNegativeButton("Cancel", (_, _) => result.TrySetResult(null));
        prompt.Show();
        return result.Task;
    }

    private sealed class CancelChoice(TaskCompletionSource<string?> result) : Java.Lang.Object, IDialogInterfaceOnCancelListener
    {
        public void OnCancel(IDialogInterface? dialog) => result.TrySetResult(null);
    }

    #endregion
}
