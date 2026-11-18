namespace Drover.App.Platforms.Android;

[Service(Exported = true)]
[IntentFilter(["org.unifiedpush.android.connector.RAISE_TO_FOREGROUND"])]
public sealed class PushDeliveryService : Service
{
    public override global::Android.OS.IBinder OnBind(Intent? intent) => new global::Android.OS.Binder();
}
