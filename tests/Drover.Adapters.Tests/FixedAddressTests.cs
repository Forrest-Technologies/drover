namespace Drover.Adapters.Tests;

/// <summary>A provider with one address says so, so a connection of that kind needs no address of its own.</summary>
public sealed class FixedAddressTests
{
    #region Public Methods

    [Fact]
    public void Venice_names_its_one_address_and_a_compatible_endpoint_names_none()
    {
        var services = new ServiceCollection();
        services.AddOpenAICompatibleProvider();
        services.AddVeniceProvider();
        var clients = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        IModelClientProvider venice = ProviderBuilders.Venice(clients);
        IModelClientProvider compatible = new OpenAICompatibleProvider(clients);

        venice.FixedAddress.Should().Be(VeniceProvider.BaseAddress);
        compatible.FixedAddress.Should().BeNull();
    }

    #endregion
}
