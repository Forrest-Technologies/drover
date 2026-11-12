namespace Drover.Data.Tests.Isolation;

/// <summary>
/// What one user must never do to another user's rows, for a table that carries <c>user_id</c>. The two users are
/// A1 and B1; the traveler belongs to both spaces and shows that a user's rows do not depend on the space.
/// </summary>
public static class UserChecks
{
    #region Reading

    public static async Task CannotSelectAnotherUsersRows(IsolationHarness harness, string tableName)
    {
        var (table, world, own, other) = await Arrange(harness, tableName);
        await using var asB = await harness.Open(null, world.UserB1);
        await using var asA = await harness.Open(null, world.UserA1);

        Expect.That(await harness.CountRow(asB, table, other) == 1, $"Control: user B1 cannot see their own row in '{tableName}'.");
        Expect.That(await harness.CountRow(asA, table, own) == 1, $"Control: user A1 cannot see their own row in '{tableName}'.");
        Expect.That(await harness.CountRow(asA, table, other) == 0, $"User A1 selected user B1's row in '{tableName}' by key.");
        var leaked = await asA.Count($"SELECT count(*) FROM {table.Quoted} WHERE user_id = @user", ("user", world.UserB1));
        Expect.That(leaked == 0, $"User A1 selected {leaked} row(s) of user B1 in '{tableName}'.");
    }

    public static async Task SeesNothingWithNoTenantSet(IsolationHarness harness, string tableName)
    {
        var (table, world, own, _) = await Arrange(harness, tableName);
        await using var asNobody = await harness.Open(null, null);
        await using var asA = await harness.Open(null, world.UserA1);

        Expect.That(await harness.CountRow(asA, table, own) == 1, $"Control: user A1 cannot see their own row in '{tableName}'.");
        var visible = await asNobody.Count($"SELECT count(*) FROM {table.Quoted}");
        Expect.That(visible == 0, $"With no tenant set, {visible} row(s) of '{tableName}' were visible.");
        var values = await harness.Compose(table, RowPlacement.OwnedBy(world.UserA1), own.Parents);
        await Expect.Refused(
            $"Insert into '{tableName}' with no tenant set",
            "42501",
            () => harness.Insert(asNobody, table, values));
    }

    /// <summary>A user's row is theirs in every space they work in, and before any space is chosen.</summary>
    public static async Task FollowsTheUserIntoEverySpace(IsolationHarness harness, string tableName)
    {
        var table = harness.Table(tableName);
        var world = await harness.NewWorld();
        var row = await harness.Seed(table, RowPlacement.OwnedBy(world.Traveler));

        foreach (var space in new Guid?[] { world.SpaceA, world.SpaceB, null })
        {
            await using var session = await harness.Open(space, world.Traveler);
            Expect.That(
                await harness.CountRow(session, table, row) == 1,
                $"The traveler cannot see their own row in '{tableName}' while working in space {space?.ToString() ?? "(none)"}.");
        }
    }

    #endregion

    #region Writing

    public static async Task CannotUpdateAnotherUsersRows(IsolationHarness harness, string tableName)
    {
        var (table, world, own, other) = await Arrange(harness, tableName);
        await using var asB = await harness.Open(null, world.UserB1);
        await using var asA = await harness.Open(null, world.UserA1);

        Expect.That(await harness.TouchRow(asB, table, other) == 1, $"Control: user B1 cannot update their own row in '{tableName}'.");
        Expect.That(await harness.TouchRow(asA, table, own) == 1, $"Control: user A1 cannot update their own row in '{tableName}'.");
        var changed = await harness.TouchRow(asA, table, other);
        Expect.That(changed == 0, $"User A1 updated {changed} row(s) of user B1 in '{tableName}'.");
    }

    public static async Task CannotDeleteAnotherUsersRows(IsolationHarness harness, string tableName)
    {
        var (table, world, own, other) = await Arrange(harness, tableName);
        await using (var asA = await harness.Open(null, world.UserA1))
        {
            Expect.That(await harness.DeleteRow(asA, table, own) == 1,
                $"Control: user A1 cannot delete their own row in '{tableName}'.");
            var deleted = await harness.DeleteRow(asA, table, other);
            Expect.That(deleted == 0, $"User A1 deleted {deleted} row(s) of user B1 in '{tableName}'.");
            await asA.Commit();
        }

        await using var asB = await harness.Open(null, world.UserB1);
        Expect.That(await harness.CountRow(asB, table, other) == 1, $"User B1's row in '{tableName}' is gone after A1's delete.");
    }

    public static async Task CannotInsertARowForAnotherUser(IsolationHarness harness, string tableName)
    {
        var (table, world, _, other) = await Arrange(harness, tableName);
        var values = await harness.Compose(table, RowPlacement.OwnedBy(world.UserB1), other.Parents);
        await using var asA = await harness.Open(null, world.UserA1);

        await Expect.Refused(
            $"User A1 inserting a row of user B1 into '{tableName}'",
            "42501",
            () => harness.Insert(asA, table, values));
    }

    public static async Task CannotGiveARowToAnotherUser(IsolationHarness harness, string tableName)
    {
        var (table, world, own, _) = await Arrange(harness, tableName);
        await using var asA = await harness.Open(null, world.UserA1);

        await Expect.Refused(
            $"User A1 giving their row in '{tableName}' to user B1",
            "42501",
            () => harness.SetColumn(asA, table, own, "user_id", world.UserB1));
    }

    #endregion

    #region Private Methods

    private static async Task<(TableUnderTest Table, TenantWorld World, SeededRow Own, SeededRow Other)> Arrange(
        IsolationHarness harness,
        string tableName)
    {
        var table = harness.Table(tableName);
        var world = await harness.NewWorld();
        var own = await harness.Seed(table, RowPlacement.OwnedBy(world.UserA1));
        var other = await harness.Seed(table, RowPlacement.OwnedBy(world.UserB1));
        return (table, world, own, other);
    }

    #endregion
}
