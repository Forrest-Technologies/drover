namespace Drover.Data.Tests.Isolation;

/// <summary>
/// What one member of a space must never do to another member's private rows, for a table that carries
/// <c>audience_user_id</c>. Users A1 and A2 share space A. A1 owns a private row and a shared row; A2 acts.
/// </summary>
public static class AudienceChecks
{
    #region Reading

    public static async Task CannotSelectAnotherMembersPrivateRows(IsolationHarness harness, string tableName)
    {
        var (table, world, mine, shared) = await Arrange(harness, tableName);
        await using var asA1 = await harness.Open(world.SpaceA, world.UserA1);
        await using var asA2 = await harness.Open(world.SpaceA, world.UserA2);

        Expect.That(await harness.CountRow(asA1, table, mine) == 1, $"Control: A1 cannot see their private row in '{tableName}'.");
        Expect.That(await harness.CountRow(asA2, table, shared) == 1, $"Control: A2 cannot see a shared row in '{tableName}'.");
        Expect.That(await harness.CountRow(asA2, table, mine) == 0, $"A2 selected A1's private row in '{tableName}' by key.");
        var leaked = await asA2.Count(
            $"SELECT count(*) FROM {table.Quoted} WHERE audience_user_id = @user",
            ("user", world.UserA1));
        Expect.That(leaked == 0, $"A2 selected {leaked} private row(s) of A1 in '{tableName}'.");
    }

    #endregion

    #region Writing

    public static async Task CannotUpdateAnotherMembersPrivateRows(IsolationHarness harness, string tableName)
    {
        var (table, world, mine, _) = await Arrange(harness, tableName);
        await using var asA1 = await harness.Open(world.SpaceA, world.UserA1);
        await using var asA2 = await harness.Open(world.SpaceA, world.UserA2);

        Expect.That(await harness.TouchRow(asA1, table, mine) == 1, $"Control: A1 cannot update their private row in '{tableName}'.");
        var changed = await harness.TouchRow(asA2, table, mine);
        Expect.That(changed == 0, $"A2 updated {changed} private row(s) of A1 in '{tableName}'.");
    }

    public static async Task CannotDeleteAnotherMembersPrivateRows(IsolationHarness harness, string tableName)
    {
        var (table, world, mine, _) = await Arrange(harness, tableName);
        await using (var asA2 = await harness.Open(world.SpaceA, world.UserA2))
        {
            var deleted = await harness.DeleteRow(asA2, table, mine);
            Expect.That(deleted == 0, $"A2 deleted {deleted} private row(s) of A1 in '{tableName}'.");
            await asA2.Commit();
        }

        await using var asA1 = await harness.Open(world.SpaceA, world.UserA1);
        Expect.That(await harness.CountRow(asA1, table, mine) == 1, $"A1's private row in '{tableName}' is gone after A2's delete.");
    }

    public static async Task CannotInsertARowAddressedToAnotherMember(IsolationHarness harness, string tableName)
    {
        var (table, world, mine, _) = await Arrange(harness, tableName);
        var addressedToA1 = new RowPlacement(world.SpaceA, world.UserA1, world.UserA2);
        var values = await harness.Compose(table, addressedToA1, mine.Parents);
        await using var asA2 = await harness.Open(world.SpaceA, world.UserA2);

        await Expect.Refused(
            $"A2 inserting a row addressed to A1 into '{tableName}'",
            "42501",
            () => harness.Insert(asA2, table, values));
    }

    public static async Task CannotHandARowToAnotherMember(IsolationHarness harness, string tableName)
    {
        var (table, world, mine, _) = await Arrange(harness, tableName);
        await using var asA1 = await harness.Open(world.SpaceA, world.UserA1);

        await Expect.Refused(
            $"A1 addressing their private row in '{tableName}' to A2",
            "42501",
            () => harness.SetColumn(asA1, table, mine, "audience_user_id", world.UserA2));
    }

    #endregion

    #region Children

    /// <summary>Only for a table whose parent has an audience: a child cannot be visible to more people than its parent.</summary>
    public static async Task ChildCannotHaveAWiderAudienceThanItsParent(IsolationHarness harness, string tableName)
    {
        var (table, world, mine, _) = await Arrange(harness, tableName);
        RequireAudienceParent(harness, table);
        await using var asA1 = await harness.Open(world.SpaceA, world.UserA1);
        var wider = await harness.Compose(table, RowPlacement.Shared(world.SpaceA, world.UserA1), mine.Parents);

        await Expect.Refused(
            $"Inserting a shared row into '{tableName}' under a private parent",
            "23503",
            () => harness.Insert(asA1, table, wider));
    }

    public static async Task ChildCannotBeWidenedBeyondItsParent(IsolationHarness harness, string tableName)
    {
        var (table, world, mine, _) = await Arrange(harness, tableName);
        RequireAudienceParent(harness, table);
        await using var asA1 = await harness.Open(world.SpaceA, world.UserA1);

        await Expect.Refused(
            $"Sharing a row of '{tableName}' while its parent stays private",
            "23503",
            () => harness.SetColumn(asA1, table, mine, "audience_user_id", Audience.Everyone));
    }

    /// <summary>Sharing the topmost parent shares every row beneath it, in one statement.</summary>
    public static async Task SharingTheParentSharesTheChild(IsolationHarness harness, string tableName)
    {
        var (table, world, mine, _) = await Arrange(harness, tableName);
        RequireAudienceParent(harness, table);
        var root = TopmostAudienceAncestor(harness, mine);
        var rootTable = harness.Table(root.Table);

        await using (var before = await harness.Open(world.SpaceA, world.UserA2))
        {
            Expect.That(await harness.CountRow(before, table, mine) == 0, $"Control: A2 can see A1's private row in '{tableName}'.");
        }

        await using (var asA1 = await harness.Open(world.SpaceA, world.UserA1))
        {
            var shared = await harness.SetColumn(asA1, rootTable, root, "audience_user_id", Audience.Everyone);
            Expect.That(shared == 1, $"A1 could not share their row in '{root.Table}'.");
            await asA1.Commit();
        }

        await using var asA2 = await harness.Open(world.SpaceA, world.UserA2);
        Expect.That(
            await harness.CountRow(asA2, table, mine) == 1,
            $"Sharing '{root.Table}' did not share the row in '{tableName}' beneath it.");
    }

    #endregion

    #region Private Methods

    private static async Task<(TableUnderTest Table, TenantWorld World, SeededRow Private, SeededRow Shared)> Arrange(
        IsolationHarness harness,
        string tableName)
    {
        var table = harness.Table(tableName);
        var world = await harness.NewWorld();
        var mine = await harness.Seed(table, RowPlacement.Private(world.SpaceA, world.UserA1));
        var shared = await harness.Seed(table, RowPlacement.Shared(world.SpaceA, world.UserA1));
        return (table, world, mine, shared);
    }

    private static void RequireAudienceParent(IsolationHarness harness, TableUnderTest table) =>
        Expect.That(
            harness.TenantParents(table, OwnershipClass.SpaceWithAudience).Count > 0,
            $"'{table.Name}' has no parent with an audience, so this check does not apply to it.");

    private static SeededRow TopmostAudienceAncestor(IsolationHarness harness, SeededRow row)
    {
        var current = row;

        while (harness.TenantParents(harness.Table(current.Table), OwnershipClass.SpaceWithAudience) is [var parent, ..])
        {
            current = current.Parents[parent.Name];
        }

        return current;
    }

    #endregion
}
