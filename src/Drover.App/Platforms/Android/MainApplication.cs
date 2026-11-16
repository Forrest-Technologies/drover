namespace Drover.App.Platforms.Android;

[global::Android.App.Application]
public sealed class MainApplication(nint handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
