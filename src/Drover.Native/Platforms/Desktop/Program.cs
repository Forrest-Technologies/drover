namespace Drover.Native;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
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
