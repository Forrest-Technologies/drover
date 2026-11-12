namespace Drover.Adapters.Tests.Support;

internal static class ProviderBuilders
{
    public static VeniceProvider Venice(IHttpClientFactory clients) => new(clients, new OpenAICompatibleProvider(clients).CreateProtocolClient, NullLogger<VeniceProvider>.Instance);
}
