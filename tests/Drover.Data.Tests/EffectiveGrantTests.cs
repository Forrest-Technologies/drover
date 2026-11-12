namespace Drover.Data.Tests;

/// <summary>
/// The SQL function <c>effective_grants(bot_id)</c> against the table of cases that <c>Drover.Domain.Tests</c> runs
/// through the C# rule, so the two cannot drift apart.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class EffectiveGrantTests(PostgresFixture postgres)
{
    #region Shared Cases

    [Theory]
    [MemberData(nameof(EffectiveGrantCases.Names), MemberType = typeof(EffectiveGrantCases))]
    public async Task The_sql_function_and_the_csharp_rule_both_give_the_expected_effective_grant(string name)
    {
        var grantCase = EffectiveGrantCases.Find(name);
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var path = await SavePath(provider, world, grantCase);

        var fromSql = await EffectiveGrant(provider, world.SpaceA, world.UserA1, path.Leaf, path.Tool);
        var fromCSharp = GrantRules.Effective(grantCase.Entries(), grantCase.OwnClosureRow);

        fromSql.Should().Be(grantCase.ExpectedEffect(), "the SQL function must give the expected grant");
        fromCSharp.Should().Be(grantCase.ExpectedEffect(), "the C# rule must give the expected grant");
    }

    #endregion

    #region Read Time

    [Fact]
    public async Task Narrowing_a_parent_narrows_the_whole_subtree_with_no_write_to_the_child()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var path = await SavePath(provider, world, EffectiveGrantCases.Find("allow all the way up, depth 3"));
        (await EffectiveGrant(provider, world.SpaceA, world.UserA1, path.Leaf, path.Tool)).Should().Be(GrantEffect.Allow);

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var grant = await session.Db.ToolGrants.SingleAsync(
                found => found.BotId == path.Bots[2].Id,
                TestContext.Current.CancellationToken);
            grant.Effect = GrantEffect.Deny;
            await session.Commit();
        }

        (await EffectiveGrant(provider, world.SpaceA, world.UserA1, path.Leaf, path.Tool)).Should().Be(GrantEffect.Deny);
    }

    [Fact]
    public async Task The_function_runs_under_row_level_security_so_another_space_sees_nothing()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var path = await SavePath(provider, world, EffectiveGrantCases.Find("allow all the way up, depth 2"));

        (await EffectiveGrant(provider, world.SpaceA, world.UserA1, path.Leaf, path.Tool)).Should().Be(GrantEffect.Allow);
        (await EffectiveGrant(provider, world.SpaceB, world.UserB1, path.Leaf, path.Tool)).Should().BeNull();
    }

    [Fact]
    public async Task The_function_gives_one_row_per_tool_for_a_bot_with_several()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var connection = CoreRows.NewConnection();
        var parent = CoreRows.NewBot(connection);
        var child = CoreRows.NewBot(connection, parent);
        var tools = new[] { CoreRows.NewTool(), CoreRows.NewTool(), CoreRows.NewTool() };
        session.Db.ModelConnections.Add(connection);
        session.Db.Tools.AddRange(tools);
        CoreRows.AddBot(session.Db, parent, []);
        CoreRows.AddBot(session.Db, child, [parent]);
        session.Db.ToolGrants.AddRange(
            Grant(parent, tools[0], GrantEffect.Allow),
            Grant(child, tools[0], GrantEffect.Allow),
            Grant(parent, tools[1], GrantEffect.Ask),
            Grant(child, tools[1], GrantEffect.Allow),
            Grant(child, tools[2], GrantEffect.Allow));
        await session.Commit();

        await using var reader = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var rows = await reader.Db.Database
            .SqlQuery<EffectiveRow>($"SELECT tool_id, effect FROM effective_grants({child.Id})")
            .ToListAsync(TestContext.Current.CancellationToken);

        rows.Should().BeEquivalentTo(
        [
            new EffectiveRow(tools[0].Id, "allow"),
            new EffectiveRow(tools[1].Id, "ask"),
        ]);
    }

    #endregion

    #region Private Methods

    private static ToolGrant Grant(Bot bot, Tool tool, GrantEffect effect, ReviewState review = ReviewState.Current) => new()
    {
        BotId = bot.Id,
        ToolId = tool.Id,
        Effect = effect,
        ReviewState = review,
    };

    /// <summary>Saves one tool and a chain of bots, the leaf first in <see cref="SavedPath.Bots"/>, with the case's grants.</summary>
    private static async Task<SavedPath> SavePath(IServiceProvider provider, TenantWorld world, GrantCase grantCase)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var connection = CoreRows.NewConnection();
        var tool = CoreRows.NewTool();
        session.Db.ModelConnections.Add(connection);
        session.Db.Tools.Add(tool);

        var bots = BuildChain(connection, grantCase.Path.Count);

        for (var index = bots.Count - 1; index >= 0; index--)
        {
            CoreRows.AddBot(session.Db, bots[index], [.. bots.Skip(index + 1)], includeOwnRow: index != 0 || grantCase.OwnClosureRow);
        }

        var entries = grantCase.Entries();

        for (var index = 0; index < bots.Count; index++)
        {
            if (entries[index] is { } entry)
            {
                session.Db.ToolGrants.Add(Grant(bots[index], tool, entry.Effect, entry.ReviewState));
            }
        }

        await session.Commit();
        return new SavedPath(bots, tool);
    }

    /// <summary>The bots from the leaf to the root, each with its parent set.</summary>
    private static List<Bot> BuildChain(ModelConnection connection, int length)
    {
        var chainRootFirst = new List<Bot>();
        Bot? parent = null;

        for (var depth = 0; depth < length; depth++)
        {
            parent = CoreRows.NewBot(connection, parent);
            chainRootFirst.Add(parent);
        }

        chainRootFirst.Reverse();
        return chainRootFirst;
    }

    private static async Task<GrantEffect?> EffectiveGrant(IServiceProvider provider, Guid spaceId, Guid userId, Bot bot, Tool tool)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        var effects = await session.Db.Database
            .SqlQuery<string>($"SELECT effect AS \"Value\" FROM effective_grants({bot.Id}) WHERE tool_id = {tool.Id}")
            .ToListAsync(TestContext.Current.CancellationToken);
        return effects is [var effect] ? GrantCase.ParseEffect(effect) : null;
    }

    #endregion

    #region Nested Types

    private sealed record SavedPath(List<Bot> Bots, Tool Tool)
    {
        public Bot Leaf => Bots[0];
    }

    private sealed record EffectiveRow(Guid ToolId, string Effect);

    #endregion
}
