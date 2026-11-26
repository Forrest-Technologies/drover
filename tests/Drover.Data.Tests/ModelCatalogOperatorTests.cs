namespace Drover.Data.Tests;

/// <summary>
/// What a space admin says about a model lives in two columns of the catalog row, apart from what the provider
/// reported, and stays inside the space.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class ModelCatalogOperatorTests(PostgresFixture postgres)
{
    #region Round Trip

    [Fact]
    public async Task What_an_admin_set_and_what_the_provider_reported_are_stored_apart_and_read_back()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var connectionId = await SaveEntry(provider, world, new ModelCatalogEntry
        {
            Model = "vendor/model",
            DisplayName = "Model",
            Capabilities = new ModelCapabilities { ContextTokens = 8192 },
            Pricing = new ModelPricing { InputPerMillion = 9m },
            OperatorCapabilities = new ModelCapabilityOverrides { ContextTokens = 20_000_000, Vision = true, ReasoningEfforts = ["low", "high"] },
            OperatorPricing = new ModelPricing { OutputPerMillion = 1.25m, Currency = "EUR" },
            LastSeenAt = DateTimeOffset.UtcNow,
        });

        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var stored = await session.Db.ModelCatalog.AsNoTracking().SingleAsync(entry => entry.ModelConnectionId == connectionId, TestContext.Current.CancellationToken);

        stored.Capabilities.ContextTokens.Should().Be(8192);
        stored.OperatorCapabilities.Should().BeEquivalentTo(new ModelCapabilityOverrides
        {
            ContextTokens = 20_000_000,
            Vision = true,
            ReasoningEfforts = ["low", "high"],
        });
        stored.EffectiveCapabilities.Should().Match<ModelCapabilities>(can => can.ContextTokens == 20_000_000 && can.Vision);
        stored.EffectivePricing.Should().Be(new ModelPricing { InputPerMillion = 9m, OutputPerMillion = 1.25m, Currency = "EUR" });
    }

    [Fact]
    public async Task A_row_nobody_set_anything_on_has_null_operator_columns()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var connectionId = await SaveEntry(
            provider,
            world,
            new ModelCatalogEntry { Model = "plain", DisplayName = "Plain", LastSeenAt = DateTimeOffset.UtcNow });

        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var stored = await session.Db.ModelCatalog.AsNoTracking().SingleAsync(entry => entry.ModelConnectionId == connectionId, TestContext.Current.CancellationToken);

        stored.OperatorCapabilities.Should().BeNull();
        stored.OperatorPricing.Should().BeNull();
        stored.EffectiveCapabilities.Should().Be(stored.Capabilities);
    }

    #endregion

    #region Isolation

    [Fact]
    public async Task Another_space_never_sees_the_operators_values()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await SaveEntry(provider, world, new ModelCatalogEntry
        {
            Model = "secret-price",
            DisplayName = "Secret price",
            OperatorPricing = new ModelPricing { InputPerMillion = 3m },
            LastSeenAt = DateTimeOffset.UtcNow,
        });

        await using var session = await TenantSession.Open(provider, world.SpaceB, world.UserB1);

        (await session.Db.ModelCatalog.AsNoTracking().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    #endregion

    #region Private Methods

    private static async Task<Guid> SaveEntry(IServiceProvider provider, TenantWorld world, ModelCatalogEntry entry)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var connection = new ModelConnection { Name = "Local", Kind = ModelConnectionKinds.Compatible, BaseUrl = "https://models.example.test/v1" };
        session.Db.ModelConnections.Add(connection);
        entry.ModelConnectionId = connection.Id;
        session.Db.ModelCatalog.Add(entry);
        await session.Db.SaveChangesAsync();
        await session.Commit();
        return connection.Id;
    }

    #endregion
}
