namespace Drover.Native;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseMacOS()
            .UseWin32()
            .Build();

        host.Run();
    }
}
