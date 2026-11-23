namespace Drover.Native;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
#if DROVER_UI_CHECKS
        if (OperatingSystem.IsMacOS())
        {
            // Hosted runners can create a Metal layer without a drawable for the window.
            Uno.UI.FeatureConfiguration.Rendering.UseMetalOnMacOS = false;
        }
#endif

        var builder = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseMacOS()
            .UseWin32();

        if (OperatingSystem.IsLinux())
        {
            builder.FontProvider(new LinuxFontProvider(SkiaBackend.CreateFontProvider()));
        }

        builder.Build().Run();
    }
}
