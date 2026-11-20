namespace Drover.Adapters.Tests.Support;

internal static class ProviderBuilders
{
    public static VeniceProvider Venice(IHttpClientFactory clients)
    {
        var compatible = new OpenAICompatibleProvider(clients);
        return new(
            clients,
            compatible.CreateProtocolClient,
            compatible.GenerateCompatibleImage,
            NullLogger<VeniceProvider>.Instance);
    }
}
