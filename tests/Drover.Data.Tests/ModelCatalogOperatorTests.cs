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
        stored.EffectivePricing.Should().Be(
            new ModelPricing { InputPerMillion = null, OutputPerMillion = 1.25m, Currency = "EUR" },
            "the provider's dollar price is not mixed with the admin's euro price");
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

    #region Migration

    [Fact]
    public async Task The_migration_moves_a_vision_mark_into_the_operator_column_and_leaves_other_rows_alone()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var connectionId = await SaveEntry(provider, world, new ModelCatalogEntry { Model = "marked", DisplayName = "Marked", LastSeenAt = DateTimeOffset.UtcNow });
        await SaveSecondEntry(provider, world, connectionId, "plain");
        await MigrateTo(scratch, "ExtensionsWithoutBots");

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            await session.Execute(
                "UPDATE model_catalog SET capabilities = '{\"vision\":true,\"vision_set_by_operator\":true,\"tool_calling\":true}'::jsonb WHERE model = 'marked'");
            await session.Execute("UPDATE model_catalog SET capabilities = '{\"vision\":true}'::jsonb WHERE model = 'plain'");
            await session.Commit();
        }

        await MigrateTo(scratch, null);

        await using (var read = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var rows = await read.Db.ModelCatalog.AsNoTracking().ToDictionaryAsync(entry => entry.Model, TestContext.Current.CancellationToken);
            rows["marked"].OperatorCapabilities.Should().Be(new ModelCapabilityOverrides { Vision = true });
            rows["marked"].Capabilities.Vision.Should().BeTrue();
            rows["plain"].OperatorCapabilities.Should().BeNull("the provider said it, not an admin");
        }

        (await ReadCapabilitiesText(provider, world, "marked")).Should().NotContain("vision_set_by_operator");
        (await PolicyIsForced(scratch, "model_catalog")).Should().BeTrue("the owner reads past the policy only inside the migration");

        await MigrateTo(scratch, "ExtensionsWithoutBots");
        (await ReadCapabilitiesText(provider, world, "marked")).Should().Contain("\"vision_set_by_operator\": true", "going back restores the old mark");
    }

    #endregion

    #region Private Methods

    private static async Task SaveSecondEntry(IServiceProvider provider, TenantWorld world, Guid connectionId, string model)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        session.Db.ModelCatalog.Add(new ModelCatalogEntry { ModelConnectionId = connectionId, Model = model, DisplayName = model, LastSeenAt = DateTimeOffset.UtcNow });
        await session.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await session.Commit();
    }

    private static async Task<string> ReadCapabilitiesText(IServiceProvider provider, TenantWorld world, string model)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var texts = await session.Db.Database
            .SqlQuery<string>($"SELECT capabilities::text AS \"Value\" FROM model_catalog WHERE model = {model}")
            .ToListAsync(TestContext.Current.CancellationToken);
        return texts.Single();
    }

    private static async Task MigrateTo(ScratchDatabase scratch, string? target)
    {
        NpgsqlConnection.ClearAllPools();
        var options = new DbContextOptionsBuilder<DroverDbContext>();
        options.UseDroverNpgsql(scratch.OwnerConnectionString);
        await using var context = new DroverDbContext(options.Options);
        await context.GetService<IMigrator>().MigrateAsync(target, TestContext.Current.CancellationToken);
        NpgsqlConnection.ClearAllPools();
    }

    private static async Task<bool> PolicyIsForced(ScratchDatabase scratch, string table)
    {
        await using var connection = new NpgsqlConnection(scratch.AppConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT relforcerowsecurity FROM pg_class WHERE relname = @table", connection);
        command.Parameters.AddWithValue("table", table);
        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

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
