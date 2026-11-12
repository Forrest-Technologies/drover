namespace Drover.Data.Tests;

/// <summary>
/// The rules every table must keep, read from <c>pg_catalog</c>. They find tables nobody thought to test: a table
/// added without a policy fails here whether or not anyone wrote a test for it.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class CatalogTests(PostgresFixture postgres)
{
    #region Ownership

    [Fact]
    public async Task Every_table_has_an_ownership_class()
    {
        var problems = await Audit(audit => audit.TablesWithoutClass());

        problems.Should().BeEmpty();
    }

    [Fact]
    public void The_tables_of_the_first_migration_are_classed_as_the_data_model_says()
    {
        var classes = OwnershipMap.Current.Tables.ToDictionary(table => table.Table, table => table.Class);

        classes.Should().Contain(
        [
            new("memberships", OwnershipClass.Space),
            new("user_claims", OwnershipClass.User),
            new("user_logins", OwnershipClass.User),
            new("user_tokens", OwnershipClass.User),
            new("spaces", OwnershipClass.Global),
            new("users", OwnershipClass.Global),
            new("__EFMigrationsHistory", OwnershipClass.Global),
        ]);
    }

    #endregion

    #region Row-Level Security

    [Fact]
    public async Task Row_level_security_is_enabled_and_forced_on_every_tenant_table()
    {
        var problems = await Audit(audit => audit.RowSecurityNotForced());

        problems.Should().BeEmpty();
    }

    [Fact]
    public async Task Every_tenant_table_has_policies_for_all_commands_with_using_and_with_check()
    {
        var problems = await Audit(audit => audit.IncompletePolicies());

        problems.Should().BeEmpty();
    }

    [Fact]
    public async Task The_discriminator_columns_are_not_nullable()
    {
        var problems = await Audit(audit => audit.NullableDiscriminators());

        problems.Should().BeEmpty();
    }

    #endregion

    #region Foreign Keys

    [Fact]
    public async Task Every_foreign_key_between_space_owned_tables_includes_space_id()
    {
        var problems = await Audit(audit => audit.ForeignKeysOutsideTheSpace());

        problems.Should().BeEmpty();
    }

    [Fact]
    public async Task Every_foreign_key_to_a_table_with_an_audience_includes_the_audience()
    {
        var problems = await Audit(audit => audit.ForeignKeysWithoutAudience());

        problems.Should().BeEmpty();
    }

    #endregion

    #region Roles And Functions

    [Fact]
    public async Task The_app_role_is_weak()
    {
        var problems = await Audit(audit => audit.AppRoleWeaknesses());

        problems.Should().BeEmpty();
    }

    [Fact]
    public async Task The_functions_in_sys_are_exactly_the_manifest()
    {
        var problems = await Audit(audit => audit.SystemFunctionDrift());

        problems.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private async Task<IReadOnlyList<string>> Audit(Func<CatalogAudit, Task<IReadOnlyList<string>>> check)
    {
        await using var connection = new NpgsqlConnection(postgres.AppConnectionString());
        await connection.OpenAsync();
        return await check(new CatalogAudit(connection, OwnershipMap.Current, ManifestFile.ReadSystemFunctions()));
    }

    #endregion
}
