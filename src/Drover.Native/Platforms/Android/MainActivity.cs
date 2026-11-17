namespace Drover.Native.Droid;

/// <summary>The one screen Android knows about. Everything a person sees is drawn inside it by the shared app.</summary>
[Android.App.Activity(
    MainLauncher = true,
    ConfigurationChanges = Uno.UI.ActivityHelper.AllConfigChanges,
    WindowSoftInputMode = Android.Views.SoftInput.AdjustNothing | Android.Views.SoftInput.StateHidden,
    Exported = true)]
public sealed class MainActivity : Uno.UI.Runtime.Android.ApplicationActivity
{
    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);
        base.OnCreate(savedInstanceState);
    }
}
