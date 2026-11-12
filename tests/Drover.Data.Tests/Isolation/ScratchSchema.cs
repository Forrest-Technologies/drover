namespace Drover.Data.Tests.Isolation;

/// <summary>A scratch entity owned by a space, standing in for a table step 5 has not made yet.</summary>
public sealed class ScratchWidget : ISpaceOwned
{
    public Guid SpaceId { get; set; }
}

/// <summary>A scratch entity with an audience, the parent of <see cref="ScratchChild"/>.</summary>
public sealed class ScratchParent : ISpaceOwned, IAudienceScoped
{
    public Guid SpaceId { get; set; }

    public Guid AudienceUserId { get; set; }
}

/// <summary>A scratch entity with an audience that must inherit it from <see cref="ScratchParent"/>.</summary>
public sealed class ScratchChild : ISpaceOwned, IAudienceScoped
{
    public Guid SpaceId { get; set; }

    public Guid AudienceUserId { get; set; }
}

/// <summary>How the scratch child points at its parent: the rule, or one of the two mistakes the suite catches.</summary>
public enum ChildKey
{
    /// <summary>Repeats the space and the audience, with <c>ON UPDATE CASCADE</c>.</summary>
    Correct,

    /// <summary>Repeats the space but not the audience, so the child could be wider than its parent.</summary>
    OmitsAudience,

    /// <summary>Repeats neither, so the child could point at another space's parent.</summary>
    OmitsSpace,
}

/// <summary>
/// Scratch tables, with and without the mistakes the suite exists to catch. They give the generated checks tables
/// with an audience to run on before the real schema has one, and give the tests below a rule to break.
/// </summary>
public static class ScratchSchema
{
    #region Private Fields

    private const string WidgetColumns =
        "id uuid PRIMARY KEY, space_id uuid NOT NULL, label text NOT NULL";

    private const string CorrectKey =
        """
        FOREIGN KEY (space_id, parent_id, audience_user_id)
          REFERENCES scratch_parent (space_id, id, audience_user_id) ON UPDATE CASCADE
        """;

    #endregion

    #region Ownership And Seeders

    public static OwnershipMap Widgets(OwnershipMap real) =>
        real.With(new TableOwnership("scratch_widgets", OwnershipClass.Space, typeof(ScratchWidget)));

    public static OwnershipMap AudiencePair(OwnershipMap real) =>
        real.With(
            new TableOwnership("scratch_parent", OwnershipClass.SpaceWithAudience, typeof(ScratchParent)),
            new TableOwnership("scratch_child", OwnershipClass.SpaceWithAudience, typeof(ScratchChild), ["scratch_parent"]));

    public static TableSeeder[] AllSeeders() => [new WidgetSeeder(), new ParentSeeder(), new ChildSeeder()];

    #endregion

    #region Tables

    /// <summary>A space-owned table with the policy the migration helper writes.</summary>
    public static async Task CreateWidgets(ScratchDatabase database, string columns = WidgetColumns)
    {
        await database.Run($"CREATE TABLE scratch_widgets ({columns})");
        await database.Run("GRANT SELECT, INSERT, UPDATE, DELETE ON scratch_widgets TO drover_app");
    }

    /// <summary>Row-level security on the table, from the helper a migration would call.</summary>
    public static Task IsolateWidgets(ScratchDatabase database) =>
        database.Migrate(migration => migration.IsolateBySpace("scratch_widgets"));

    /// <summary>A parent and child that both have an audience, with the child's foreign key written as <paramref name="key"/>.</summary>
    public static async Task CreateAudiencePair(ScratchDatabase database, ChildKey key)
    {
        await database.Run(
            """
            CREATE TABLE scratch_parent (
              id uuid PRIMARY KEY, space_id uuid NOT NULL, audience_user_id uuid NOT NULL, label text NOT NULL,
              UNIQUE (id), UNIQUE (space_id, id), UNIQUE (space_id, id, audience_user_id));
            """);
        var foreignKey = key switch
        {
            ChildKey.Correct => CorrectKey,
            ChildKey.OmitsAudience => "FOREIGN KEY (space_id, parent_id) REFERENCES scratch_parent (space_id, id)",
            _ => "FOREIGN KEY (parent_id) REFERENCES scratch_parent (id)",
        };
        await database.Run(
            $"""
            CREATE TABLE scratch_child (
              id uuid PRIMARY KEY, space_id uuid NOT NULL, audience_user_id uuid NOT NULL, parent_id uuid NOT NULL,
              label text NOT NULL, {foreignKey});
            """);
        await database.Migrate(migration => migration
            .IsolateBySpace("scratch_parent", audience: true)
            .IsolateBySpace("scratch_child", audience: true));
    }

    #endregion

    #region Seeders

    private sealed class WidgetSeeder : TableSeeder<ScratchWidget>
    {
        public override Task<IReadOnlyDictionary<string, object?>> Row(SeedRow seed) =>
            Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>
            {
                ["id"] = Guid.CreateVersion7(),
                ["label"] = seed.Unique("widget"),
            });
    }

    private sealed class ParentSeeder : TableSeeder<ScratchParent>
    {
        public override Task<IReadOnlyDictionary<string, object?>> Row(SeedRow seed) =>
            Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>
            {
                ["id"] = Guid.CreateVersion7(),
                ["label"] = seed.Unique("parent"),
            });
    }

    private sealed class ChildSeeder : TableSeeder<ScratchChild>
    {
        public override Task<IReadOnlyDictionary<string, object?>> Row(SeedRow seed) =>
            Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>
            {
                ["id"] = Guid.CreateVersion7(),
                ["parent_id"] = seed.Parent("scratch_parent"),
                ["label"] = seed.Unique("child"),
            });
    }

    #endregion
}
