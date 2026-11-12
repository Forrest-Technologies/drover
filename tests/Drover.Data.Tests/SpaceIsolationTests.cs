namespace Drover.Data.Tests;

/// <summary>
/// One case per table that carries <c>space_id</c>, found from the entity model: space A must never read, change,
/// delete, insert into, move rows into, or point at the rows of space B. See <see cref="SpaceChecks"/>.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class SpaceIsolationTests(IsolationFixture isolation) : IClassFixture<IsolationFixture>
{
    #region Public Methods

    [Theory]
    [MemberData(nameof(IsolationTables.Space), MemberType = typeof(IsolationTables))]
    public Task Space_A_cannot_select_space_Bs_rows(string table) =>
        SpaceChecks.CannotSelectAnotherSpacesRows(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.Space), MemberType = typeof(IsolationTables))]
    public Task Space_A_cannot_update_space_Bs_rows(string table) =>
        SpaceChecks.CannotUpdateAnotherSpacesRows(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.Space), MemberType = typeof(IsolationTables))]
    public Task Space_A_cannot_delete_space_Bs_rows(string table) =>
        SpaceChecks.CannotDeleteAnotherSpacesRows(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.Space), MemberType = typeof(IsolationTables))]
    public Task Space_A_cannot_insert_a_row_with_space_Bs_id(string table) =>
        SpaceChecks.CannotInsertWithAnotherSpacesId(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.Space), MemberType = typeof(IsolationTables))]
    public Task Space_A_cannot_move_a_row_into_space_B(string table) =>
        SpaceChecks.CannotMoveARowIntoAnotherSpace(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.SpaceWithSpaceOwnedParent), MemberType = typeof(IsolationTables))]
    public Task Space_A_cannot_reference_a_parent_row_of_space_B(string table) =>
        SpaceChecks.CannotReferenceAnotherSpacesParent(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.Space), MemberType = typeof(IsolationTables))]
    public Task With_no_tenant_set_nothing_is_visible_and_inserts_fail(string table) =>
        SpaceChecks.SeesNothingWithNoTenantSet(isolation.For(table), table);

    [Theory]
    [MemberData(nameof(IsolationTables.Space), MemberType = typeof(IsolationTables))]
    public Task The_app_cannot_truncate_a_tenant_table(string table) =>
        SpaceChecks.CannotTruncate(isolation.For(table), table);

    #endregion
}
