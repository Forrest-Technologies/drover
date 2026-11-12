namespace Drover.Data.Tests;

/// <summary>
/// One physical connection serves a thousand transactions for alternating tenants, some of them rolled back and some
/// of them failed. No transaction may ever see another tenant's rows, and the identity must be gone from the
/// connection the moment each transaction ends.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class PoolingTests(PostgresFixture postgres)
{
    #region Private Fields

    private const int Transactions = 1000;

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_thousand_alternating_transactions_on_one_connection_never_cross_tenants()
    {
        await using var seedServices = postgres.BuildServices();
        var world = await TenantWorld.Seed(seedServices);
        var tenants = new Tenant[]
        {
            new(world.SpaceA, world.UserA1, [world.SpaceA]),
            new(world.SpaceB, world.UserB1, [world.SpaceB]),
            new(null, null, []),
            new(world.SpaceA, world.UserA2, [world.SpaceA]),
            new(world.SpaceB, world.Traveler, [world.SpaceB]),
            new(null, world.Traveler, []),
            new(world.SpaceA, world.Traveler, [world.SpaceA]),
        };
        await using var provider = postgres.BuildServices(maxPoolSize: 1, noResetOnClose: true);
        var seen = new List<string>();
        var backends = new HashSet<int>();

        for (var index = 0; index < Transactions; index++)
        {
            var tenant = tenants[index % tenants.Length];
            var ending = (Ending)(index / tenants.Length % Enum.GetValues<Ending>().Length);
            backends.Add(await RunOne(provider, tenant, ending, seen));
            seen.AddRange(await IdentityLeftOnTheConnection(index));
        }

        seen.Should().BeEmpty();
        backends.Should().ContainSingle("a pool of one hands every transaction the same physical connection");
    }

    #endregion

    #region Private Methods

    private static async Task<int> RunOne(ServiceProvider provider, Tenant tenant, Ending ending, List<string> seen)
    {
        await using var session = await TenantSession.Open(provider, tenant.SpaceId, tenant.UserId);
        var visible = await session.Db.Spaces.Select(space => space.Id).ToListAsync();

        if (!visible.OrderBy(id => id).SequenceEqual(tenant.ExpectedSpaces.OrderBy(id => id)))
        {
            seen.Add($"Tenant {tenant.SpaceId}/{tenant.UserId} saw spaces [{string.Join(", ", visible)}].");
        }

        var pid = await session.BackendPid();
        await Finish(session, tenant, ending);
        return pid;
    }

    private static async Task Finish(TenantSession session, Tenant tenant, Ending ending)
    {
        switch (ending)
        {
            case Ending.Commit:
                await session.Commit();
                break;
            case Ending.FailOnTheServer:
                await Record.ExceptionAsync(() => session.Db.Database.ExecuteSqlRawAsync("SELECT 1 / 0"));
                break;
            case Ending.RefusedByPolicy:
                await Record.ExceptionAsync(() => session.Execute(
                    "INSERT INTO memberships (id, space_id, user_id, role) VALUES (@id, @space, @user, 'member')",
                    ("id", Guid.CreateVersion7()),
                    ("space", Guid.CreateVersion7()),
                    ("user", tenant.UserId ?? Guid.CreateVersion7())));
                break;
            default:
                await session.Db.Database.ExecuteSqlRawAsync("UPDATE spaces SET name = name");
                break;
        }
    }

    /// <summary>Reads the settings on the one physical connection after the transaction ended, outside any transaction.</summary>
    private async Task<IEnumerable<string>> IdentityLeftOnTheConnection(int index)
    {
        await using var connection = new NpgsqlConnection(postgres.AppConnectionString(maxPoolSize: 1, noResetOnClose: true));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT coalesce(current_setting('drover.space_id', true), ''), coalesce(current_setting('drover.user_id', true), '')",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        var (space, user) = (reader.GetString(0), reader.GetString(1));
        return space.Length + user.Length == 0
            ? []
            : [$"Transaction {index} left space '{space}' and user '{user}' on the connection."];
    }

    #endregion

    #region Nested Types

    private enum Ending
    {
        Commit,
        RollBack,
        FailOnTheServer,
        RefusedByPolicy,
    }

    private sealed record Tenant(Guid? SpaceId, Guid? UserId, Guid[] ExpectedSpaces);

    #endregion
}
