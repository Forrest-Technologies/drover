namespace Drover.Data.Tests;

/// <summary>
/// One case per table that carries <c>user_id</c>: user A1 must never read, change, delete, insert, or take over
/// the rows of user B1, and a user's rows follow them into every space. See <see cref="UserChecks"/>.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class UserIsolationTests(IsolationFixture isolation) : IClassFixture<IsolationFixture>
{
    #region Public Methods

    [Theory]
    [MemberData(nameof(IsolationTables.User), MemberType = typeof(IsolationTables))]
    public Task User_A1_cannot_select_user_B1s_rows(string table) =>
        UserChecks.CannotSelectAnotherUsersRows(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.User), MemberType = typeof(IsolationTables))]
    public Task User_A1_cannot_update_user_B1s_rows(string table) =>
        UserChecks.CannotUpdateAnotherUsersRows(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.User), MemberType = typeof(IsolationTables))]
    public Task User_A1_cannot_delete_user_B1s_rows(string table) =>
        UserChecks.CannotDeleteAnotherUsersRows(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.User), MemberType = typeof(IsolationTables))]
    public Task User_A1_cannot_insert_a_row_for_user_B1(string table) =>
        UserChecks.CannotInsertARowForAnotherUser(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.User), MemberType = typeof(IsolationTables))]
    public Task User_A1_cannot_give_a_row_to_user_B1(string table) =>
        UserChecks.CannotGiveARowToAnotherUser(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.User), MemberType = typeof(IsolationTables))]
    public Task A_users_rows_are_theirs_in_every_space(string table) =>
        UserChecks.FollowsTheUserIntoEverySpace(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.User), MemberType = typeof(IsolationTables))]
    public Task With_no_tenant_set_nothing_is_visible_and_inserts_fail(string table) =>
        UserChecks.SeesNothingWithNoTenantSet(isolation.For(table), table);

    #endregion
}
