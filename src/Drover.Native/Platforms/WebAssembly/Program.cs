namespace Drover.Native;

public static class Program
{
    public static async Task Main()
    {
        // The host reads this before constructing App. Keep the semantic tree available without a hidden opt-in.
        Uno.UI.FeatureConfiguration.AutomationPeer.AutoEnableAccessibility = true;
        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseWebAssembly()
            .Build();

        await host.RunAsync();
    }
}
