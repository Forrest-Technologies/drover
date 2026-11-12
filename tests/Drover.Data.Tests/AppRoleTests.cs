namespace Drover.Data.Tests;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class AppRoleTests(PostgresFixture postgres)
{
    #region Role Attributes

    [Fact]
    public async Task The_app_role_has_no_superuser_no_bypass_and_no_creation_rights()
    {
        await using var connection = await OpenAsApp();

        var row = await ReadRole(connection, "drover_app");

        row.Should().Be(new RoleRow(Super: false, BypassRls: false, CreateDb: false, CreateRole: false, CanLogin: true));
    }

    [Fact]
    public async Task The_system_role_bypasses_row_level_security_but_cannot_log_in()
    {
        await using var connection = await OpenAsApp();

        var row = await ReadRole(connection, "drover_system");

        row.Should().Be(new RoleRow(Super: false, BypassRls: true, CreateDb: false, CreateRole: false, CanLogin: false));
    }

    [Fact]
    public async Task Nobody_can_log_in_as_the_system_role()
    {
        await using var connection = new NpgsqlConnection(postgres.RoleConnectionString("drover_system", "any"));

        var open = () => connection.OpenAsync();

        var thrown = await open.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().BeOneOf("28000", "28P01");
    }

    [Fact]
    public async Task The_bootstrap_can_run_again_and_at_the_same_time_from_several_servers()
    {
        await Task.WhenAll(postgres.Bootstrap(), postgres.Bootstrap(), postgres.Bootstrap());

        await using var connection = await OpenAsApp();
        (await ReadRole(connection, "drover_app")).Super.Should().BeFalse();
    }

    [Theory]
    [InlineData("pg_class", "relowner")]
    [InlineData("pg_proc", "proowner")]
    [InlineData("pg_namespace", "nspowner")]
    [InlineData("pg_type", "typowner")]
    [InlineData("pg_database", "datdba")]
    public async Task The_app_role_owns_nothing(string catalog, string ownerColumn)
    {
        await using var connection = await OpenAsApp();

        var owned = await Scalar<long>(
            connection,
            $"SELECT count(*) FROM pg_catalog.{catalog} WHERE {ownerColumn} = (SELECT oid FROM pg_roles WHERE rolname = 'drover_app')");

        owned.Should().Be(0);
    }

    #endregion

    #region Forbidden Statements

    [Theory]
    [InlineData("ALTER TABLE public.memberships DISABLE ROW LEVEL SECURITY")]
    [InlineData("ALTER TABLE public.memberships NO FORCE ROW LEVEL SECURITY")]
    [InlineData("DROP POLICY space_isolation ON public.memberships")]
    [InlineData("SET ROLE drover_system")]
    [InlineData("SET ROLE drover_owner")]
    [InlineData("SET SESSION AUTHORIZATION drover_system")]
    [InlineData("ALTER ROLE drover_app BYPASSRLS")]
    [InlineData("CREATE TABLE public.leak (id int)")]
    [InlineData("CREATE TEMPORARY TABLE leak (id int)")]
    [InlineData("CREATE SCHEMA leak")]
    [InlineData("CREATE FUNCTION sys.leak() RETURNS int LANGUAGE sql AS 'SELECT 1'")]
    [InlineData("CREATE FUNCTION public.leak() RETURNS int LANGUAGE sql AS 'SELECT 1'")]
    [InlineData("DROP FUNCTION sys.current_space()")]
    public async Task The_app_role_is_refused(string statement)
    {
        await using var connection = await OpenAsApp();

        var run = () => Execute(connection, statement);

        var thrown = await run.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be("42501", $"'{statement}' must be refused for lack of privilege");
    }

    [Fact]
    public async Task Turning_row_security_off_for_the_session_makes_a_query_fail_instead_of_skipping_the_policy()
    {
        await using var connection = await OpenAsApp();
        await Execute(connection, "SET row_security = off");

        var run = () => Scalar<long>(connection, "SELECT count(*) FROM public.memberships");

        var thrown = await run.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task The_app_role_cannot_read_the_migration_history()
    {
        await using var connection = await OpenAsApp();

        var run = () => Scalar<long>(connection, "SELECT count(*) FROM public.\"__EFMigrationsHistory\"");

        var thrown = await run.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be("42501");
    }

    #endregion

    #region Helpers

    private async Task<NpgsqlConnection> OpenAsApp()
    {
        var connection = new NpgsqlConnection(postgres.AppConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<RoleRow> ReadRole(NpgsqlConnection connection, string role)
    {
        await using var command = new NpgsqlCommand(
            "SELECT rolsuper, rolbypassrls, rolcreatedb, rolcreaterole, rolcanlogin FROM pg_roles WHERE rolname = @role",
            connection);
        command.Parameters.AddWithValue("role", role);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return new RoleRow(
            reader.GetBoolean(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4));
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private sealed record RoleRow(bool Super, bool BypassRls, bool CreateDb, bool CreateRole, bool CanLogin);

    #endregion
}
