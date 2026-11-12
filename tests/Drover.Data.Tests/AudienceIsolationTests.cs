namespace Drover.Data.Tests;

/// <summary>
/// One case per table that carries <c>audience_user_id</c>: inside one space, user 2 must never read, change,
/// delete, or address a row to user 1, and a child must never reach more people than its parent. The real schema
/// has no such table until step 5, so the cases run on a scratch parent and child as well. See <see cref="AudienceChecks"/>.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class AudienceIsolationTests(IsolationFixture isolation) : IClassFixture<IsolationFixture>
{
    #region Public Methods

    [Theory]
    [MemberData(nameof(IsolationTables.Audience), MemberType = typeof(IsolationTables))]
    public Task User_2_cannot_select_user_1s_private_rows(string table) =>
        AudienceChecks.CannotSelectAnotherMembersPrivateRows(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.Audience), MemberType = typeof(IsolationTables))]
    public Task User_2_cannot_update_user_1s_private_rows(string table) =>
        AudienceChecks.CannotUpdateAnotherMembersPrivateRows(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.Audience), MemberType = typeof(IsolationTables))]
    public Task User_2_cannot_delete_user_1s_private_rows(string table) =>
        AudienceChecks.CannotDeleteAnotherMembersPrivateRows(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.Audience), MemberType = typeof(IsolationTables))]
    public Task User_2_cannot_insert_a_row_addressed_to_user_1(string table) =>
        AudienceChecks.CannotInsertARowAddressedToAnotherMember(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.Audience), MemberType = typeof(IsolationTables))]
    public Task User_1_cannot_hand_a_private_row_to_user_2(string table) =>
        AudienceChecks.CannotHandARowToAnotherMember(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.AudienceWithAudienceParent), MemberType = typeof(IsolationTables))]
    public Task A_child_cannot_be_inserted_with_a_wider_audience_than_its_parent(string table) =>
        AudienceChecks.ChildCannotHaveAWiderAudienceThanItsParent(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.AudienceWithAudienceParent), MemberType = typeof(IsolationTables))]
    public Task A_child_cannot_be_shared_while_its_parent_is_private(string table) =>
        AudienceChecks.ChildCannotBeWidenedBeyondItsParent(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.AudienceWithAudienceParent), MemberType = typeof(IsolationTables))]
    public Task Sharing_the_parent_shares_the_child(string table) =>
        AudienceChecks.SharingTheParentSharesTheChild(isolation.For(table), table);

    #endregion
}
