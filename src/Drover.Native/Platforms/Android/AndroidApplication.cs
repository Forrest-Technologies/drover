namespace Drover.Native.Droid;

/// <summary>The Android process's entry: it hosts the one app every platform shares.</summary>
[Android.App.Application(
    Label = "@string/ApplicationName",
    Icon = "@mipmap/icon",
    LargeHeap = true,
    HardwareAccelerated = true,
    AllowBackup = false,
    Theme = "@style/Theme.App.Starting")]
public sealed class AndroidApplication(nint javaReference, Android.Runtime.JniHandleOwnership transfer)
    : Uno.UI.Runtime.Android.NativeApplication(javaReference, transfer)
{
    protected override UnoPlatformHost CreateHost() =>
        UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseAndroid()
            .Build();
}
