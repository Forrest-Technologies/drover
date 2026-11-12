namespace Drover.Data.Tests.Isolation;

/// <summary>
/// What one space must never do to another space's rows, for a table that carries <c>space_id</c>. Each check
/// makes a row in space A and one in space B, acts as a member of A, and throws <see cref="IsolationViolationException"/>
/// when the database allows what it must refuse. Every check also proves the row it cannot reach does exist.
/// </summary>
public static class SpaceChecks
{
    #region Reading

    public static async Task CannotSelectAnotherSpacesRows(IsolationHarness harness, string tableName)
    {
        var (table, world, own, other) = await Arrange(harness, tableName);
        await using var asB = await harness.Open(world.SpaceB, world.UserB1);
        await using var asA = await harness.Open(world.SpaceA, world.UserA1);

        Expect.That(await harness.CountRow(asB, table, other) == 1, $"Control: space B cannot see its own row in '{tableName}'.");
        Expect.That(await harness.CountRow(asA, table, own) == 1, $"Control: space A cannot see its own row in '{tableName}'.");
        Expect.That(await harness.CountRow(asA, table, other) == 0, $"Space A selected space B's row in '{tableName}' by key.");
        var leaked = await asA.Count($"SELECT count(*) FROM {table.Quoted} WHERE space_id = @space", ("space", world.SpaceB));
        Expect.That(leaked == 0, $"Space A selected {leaked} row(s) of space B in '{tableName}'.");
    }

    public static async Task SeesNothingWithNoTenantSet(IsolationHarness harness, string tableName)
    {
        var (table, world, own, _) = await Arrange(harness, tableName);
        await using var asNobody = await harness.Open(null, null);
        await using var asA = await harness.Open(world.SpaceA, world.UserA1);

        Expect.That(await harness.CountRow(asA, table, own) == 1, $"Control: space A cannot see its own row in '{tableName}'.");
        var visible = await asNobody.Count($"SELECT count(*) FROM {table.Quoted}");
        Expect.That(visible == 0, $"With no tenant set, {visible} row(s) of '{tableName}' were visible.");
        var values = await harness.Compose(table, RowPlacement.Shared(world.SpaceA, world.UserA1), own.Parents);
        await Expect.Refused(
            $"Insert into '{tableName}' with no tenant set",
            "42501",
            () => harness.Insert(asNobody, table, values));
    }

    #endregion

    #region Writing

    public static async Task CannotUpdateAnotherSpacesRows(IsolationHarness harness, string tableName)
    {
        var (table, world, own, other) = await Arrange(harness, tableName);
        if (table.Grants.AppendOnly)
        {
            await RefuseMutation(harness, table, world, own, other, harness.TouchRow);
            return;
        }

        await using var asB = await harness.Open(world.SpaceB, world.UserB1);
        await using var asA = await harness.Open(world.SpaceA, world.UserA1);

        Expect.That(await harness.TouchRow(asB, table, other) == 1, $"Control: space B cannot update its own row in '{tableName}'.");
        Expect.That(await harness.TouchRow(asA, table, own) == 1, $"Control: space A cannot update its own row in '{tableName}'.");
        var changed = await harness.TouchRow(asA, table, other);
        Expect.That(changed == 0, $"Space A updated {changed} row(s) of space B in '{tableName}'.");
    }

    public static async Task CannotDeleteAnotherSpacesRows(IsolationHarness harness, string tableName)
    {
        var (table, world, own, other) = await Arrange(harness, tableName);
        if (table.Grants.AppendOnly)
        {
            await RefuseMutation(harness, table, world, own, other, harness.DeleteRow);
            return;
        }

        await using (var asA = await harness.Open(world.SpaceA, world.UserA1))
        {
            Expect.That(await harness.DeleteRow(asA, table, own) == 1,
                $"Control: space A cannot delete its own row in '{tableName}'.");
            var deleted = await harness.DeleteRow(asA, table, other);
            Expect.That(deleted == 0, $"Space A deleted {deleted} row(s) of space B in '{tableName}'.");
            await asA.Commit();
        }

        await using var asB = await harness.Open(world.SpaceB, world.UserB1);
        Expect.That(await harness.CountRow(asB, table, other) == 1, $"Space B's row in '{tableName}' is gone after space A's delete.");
    }

    public static async Task CannotInsertWithAnotherSpacesId(IsolationHarness harness, string tableName)
    {
        var (table, world, _, other) = await Arrange(harness, tableName);
        var values = await harness.Compose(table, RowPlacement.Shared(world.SpaceB, world.UserB1), other.Parents);
        await using var asA = await harness.Open(world.SpaceA, world.UserA1);

        await Expect.Refused(
            $"Space A inserting a row of space B into '{tableName}'",
            "42501",
            () => harness.Insert(asA, table, values));
    }

    public static async Task CannotMoveARowIntoAnotherSpace(IsolationHarness harness, string tableName)
    {
        var (table, world, own, _) = await Arrange(harness, tableName);
        await using var asA = await harness.Open(world.SpaceA, world.UserA1);

        await Expect.Refused(
            $"Space A moving its row in '{tableName}' into space B",
            "42501",
            () => harness.SetColumn(asA, table, own, "space_id", world.SpaceB));
    }

    #endregion

    #region Truncation

    public static async Task CannotTruncate(IsolationHarness harness, string tableName)
    {
        var (table, world, own, other) = await Arrange(harness, tableName);
        await RefuseMutation(harness, table, world, own, other,
            (session, target, _) => session.Execute($"TRUNCATE {target.Quoted}"));
    }

    #endregion

    #region Parents

    /// <summary>Only for a table with a space-owned parent: a row in space A may not point at space B's parent row.</summary>
    public static async Task CannotReferenceAnotherSpacesParent(IsolationHarness harness, string tableName)
    {
        var (table, world, own, other) = await Arrange(harness, tableName);
        var parents = harness.TenantParents(table, OwnershipClass.Space, OwnershipClass.SpaceWithAudience);
        Expect.That(parents.Count > 0, $"'{tableName}' has no space-owned parent, so this check does not apply to it.");

        foreach (var parent in parents)
        {
            var pointing = new Dictionary<string, SeededRow>(own.Parents) { [parent.Name] = other.Parents[parent.Name] };
            var values = await harness.Compose(table, RowPlacement.Shared(world.SpaceA, world.UserA1), pointing);
            await using var asA = await harness.Open(world.SpaceA, world.UserA1);

            await Expect.Refused(
                $"Space A inserting a row into '{tableName}' that points at space B's row in '{parent.Name}'",
                "23503",
                () => harness.Insert(asA, table, values));
        }
    }

    #endregion

    #region Private Methods

    private static async Task RefuseMutation(
        IsolationHarness harness,
        TableUnderTest table,
        TenantWorld world,
        SeededRow own,
        SeededRow other,
        Func<TenantSession, TableUnderTest, SeededRow, Task<int>> mutate)
    {
        await using var asA = await harness.Open(world.SpaceA, world.UserA1);
        await using var asB = await harness.Open(world.SpaceB, world.UserB1);
        var ownBefore = await harness.Snapshot(asA, table, own);
        var otherBefore = await harness.Snapshot(asB, table, other);
        Expect.That(ownBefore.Count == 1 && otherBefore.Count == 1, $"Control: missing rows in '{table.Name}'.");

        SeededRow[] rows = [own, other];
        foreach (var row in rows)
        {
            await using var attempt = await harness.Open(world.SpaceA, world.UserA1);
            await Expect.Refused($"Mutating append-only or truncating table '{table.Name}'", "42501",
                () => mutate(attempt, table, row));
        }

        (await harness.Snapshot(asA, table, own)).Should().BeEquivalentTo(ownBefore);
        (await harness.Snapshot(asB, table, other)).Should().BeEquivalentTo(otherBefore);
    }

    private static async Task<(TableUnderTest Table, TenantWorld World, SeededRow Own, SeededRow Other)> Arrange(
        IsolationHarness harness,
        string tableName)
    {
        var table = harness.Table(tableName);
        var world = await harness.NewWorld();
        var own = await harness.Seed(table, RowPlacement.Shared(world.SpaceA, world.UserA1));
        var other = await harness.Seed(table, RowPlacement.Shared(world.SpaceB, world.UserB1));
        return (table, world, own, other);
    }

    #endregion
}
