namespace Drover.Data.Tests;

/// <summary>Named proof that account appearance rows are isolated by PostgreSQL user policies.</summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class UserAppearanceIsolationTests(IsolationFixture isolation) : IClassFixture<IsolationFixture>
{
    #region Public Methods

    [Fact]
    public async Task Isolates_user_appearance_strictly_by_user_id()
    {
        var table = "user_appearance";
        var harness = isolation.For(table);

        await UserChecks.CannotSelectAnotherUsersRows(harness, table);
        await UserChecks.CannotUpdateAnotherUsersRows(harness, table);
        await UserChecks.CannotDeleteAnotherUsersRows(harness, table);
        await UserChecks.CannotInsertARowForAnotherUser(harness, table);
        await UserChecks.CannotGiveARowToAnotherUser(harness, table);
        await UserChecks.FollowsTheUserIntoEverySpace(harness, table);
        await UserChecks.SeesNothingWithNoTenantSet(harness, table);
    }

    #endregion
}
