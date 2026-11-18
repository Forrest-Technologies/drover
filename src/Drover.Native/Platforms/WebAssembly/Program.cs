namespace Drover.Native;

public static class Program
{
    public static async Task Main()
    {
        // The host reads this before constructing App. Keep the semantic tree available without a hidden opt-in.
        Uno.UI.FeatureConfiguration.AutomationPeer.AutoEnableAccessibility = true;
        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            // The platform would fetch fonts for characters Inter lacks from another site at run time. The content
            // security policy refuses that (connect-src 'self'), and a page must not ask another site for anything,
            // so fallback stays off: such a character is drawn as missing, exactly as it was when the fetch failed.
            .FontProvider(Uno.UI.Composition.Skia.SkiaBackend.CreateFontProvider(enableFallback: false))
            .UseWebAssembly()
            .Build();

        await host.RunAsync();
    }
}
