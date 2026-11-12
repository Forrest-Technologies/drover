namespace Drover.Data.Tests;

[Collection(PostgresFixtureDefinition.Name)]
public sealed class GrantMutationTests(PostgresFixture postgres)
{
    #region Public Methods

    [Theory]
    [InlineData("SELECT")]
    [InlineData("INSERT")]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    public async Task Missing_mutable_grants_fail_until_restored(string privilege)
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await ScratchSchema.CreateWidgets(scratch);
        await ScratchSchema.IsolateWidgets(scratch);
        var ownership = ScratchSchema.Widgets(OwnershipMap.Current);
        await scratch.Run($"REVOKE {privilege} ON scratch_widgets FROM drover_app");

        (await scratch.Audit(ownership, audit => audit.AppGrantDrift()))
            .Should().ContainMatch($"*scratch_widgets*{privilege}*");

        await scratch.Run($"GRANT {privilege} ON scratch_widgets TO drover_app");

        (await scratch.Audit(ownership, audit => audit.AppGrantDrift())).Should().BeEmpty();
    }

    [Theory]
    [InlineData("UPDATE")]
    [InlineData("UPDATE (action)")]
    [InlineData("DELETE")]
    [InlineData("TRUNCATE")]
    [InlineData("TRIGGER")]
    [InlineData("REFERENCES")]
    [InlineData("REFERENCES (action)")]
    public async Task Extra_append_only_grants_fail_until_revoked(string privilege)
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await scratch.Run($"GRANT {privilege} ON audit_log TO drover_app");

        (await scratch.Audit(OwnershipMap.Current, audit => audit.AppGrantDrift()))
            .Should().ContainMatch("*audit_log*");

        await scratch.Run($"REVOKE {privilege} ON audit_log FROM drover_app");

        (await scratch.Audit(OwnershipMap.Current, audit => audit.AppGrantDrift())).Should().BeEmpty();
    }

    [Theory]
    [InlineData("REVOKE UPDATE (title) ON threads FROM drover_app",
        "GRANT UPDATE (title) ON threads TO drover_app", "*threads.title*")]
    [InlineData("GRANT UPDATE (owner_user_id) ON threads TO drover_app",
        "REVOKE UPDATE (owner_user_id) ON threads FROM drover_app", "*threads.owner_user_id*")]
    [InlineData("GRANT UPDATE ON threads TO drover_app",
        """
        REVOKE UPDATE ON threads FROM drover_app;
        GRANT UPDATE (id, space_id, audience_user_id, bot_id, title, origin, last_activity_at, archived_at)
            ON threads TO drover_app
        """,
        "*Table 'threads'*UPDATE*")]
    public async Task Precise_thread_update_grants_are_required(string mutation, string repair, string problem)
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await scratch.Run(mutation);

        (await scratch.Audit(OwnershipMap.Current, audit => audit.AppGrantDrift())).Should().ContainMatch(problem);

        await scratch.Run(repair);

        (await scratch.Audit(OwnershipMap.Current, audit => audit.AppGrantDrift())).Should().BeEmpty();
    }

    [Theory]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    public async Task Mutable_behavior_requires_own_row_write_controls(string privilege)
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await ScratchSchema.CreateWidgets(scratch);
        await ScratchSchema.IsolateWidgets(scratch);
        var ownership = ScratchSchema.Widgets(OwnershipMap.Current);
        await using var harness = await scratch.Harness(ownership, ScratchSchema.AllSeeders());
        await scratch.Run($"REVOKE {privilege} ON scratch_widgets FROM drover_app");
        var check = () => privilege == "UPDATE"
            ? SpaceChecks.CannotUpdateAnotherSpacesRows(harness, "scratch_widgets")
            : SpaceChecks.CannotDeleteAnotherSpacesRows(harness, "scratch_widgets");

        var thrown = await check.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be("42501");

        await scratch.Run($"GRANT {privilege} ON scratch_widgets TO drover_app");

        await check();
    }

    [Theory]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    public async Task Append_only_behavior_rejects_accidental_mutability(string privilege)
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var harness = await scratch.Harness(OwnershipMap.Current, [.. SeederRegistry.Discover()]);
        await scratch.Run($"GRANT {privilege} ON audit_log TO drover_app");
        var check = () => privilege == "UPDATE"
            ? SpaceChecks.CannotUpdateAnotherSpacesRows(harness, "audit_log")
            : SpaceChecks.CannotDeleteAnotherSpacesRows(harness, "audit_log");

        await check.Should().ThrowAsync<IsolationViolationException>();

        await scratch.Run($"REVOKE {privilege} ON audit_log FROM drover_app");

        await check();
    }

    #endregion
}
