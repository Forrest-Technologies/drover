namespace Drover.Data.Tests;

/// <summary>
/// The suite must catch the mistakes it exists to catch. Each test breaks one rule in a scratch database, shows
/// the catalog audit or a generated check failing, corrects the break, and shows it passing. The real schema is
/// never touched.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class ScratchTableTests(PostgresFixture postgres)
{
    #region Tables Without Policies

    [Fact]
    public async Task A_scratch_table_with_no_policy_fails_the_catalog_tests_until_it_is_isolated()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await ScratchSchema.CreateWidgets(scratch);
        var ownership = ScratchSchema.Widgets(OwnershipMap.Current);
        await using var harness = await scratch.Harness(ownership, ScratchSchema.AllSeeders());

        (await scratch.Audit(ownership, audit => audit.RowSecurityNotForced())).Should().ContainMatch("*scratch_widgets*");
        (await scratch.Audit(ownership, audit => audit.IncompletePolicies())).Should().ContainMatch("*scratch_widgets*");
        var leak = () => SpaceChecks.CannotSelectAnotherSpacesRows(harness, "scratch_widgets");
        await leak.Should().ThrowAsync<IsolationViolationException>();

        await ScratchSchema.IsolateWidgets(scratch);

        (await scratch.Audit(ownership, audit => audit.RowSecurityNotForced())).Should().BeEmpty();
        (await scratch.Audit(ownership, audit => audit.IncompletePolicies())).Should().BeEmpty();
        await using var corrected = await scratch.Harness(ownership, ScratchSchema.AllSeeders());
        await RunSpaceChecks(corrected, "scratch_widgets");
    }

    [Fact]
    public async Task A_scratch_table_the_model_does_not_know_fails_the_catalog_tests_until_it_is_classed()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await ScratchSchema.CreateWidgets(scratch);
        await ScratchSchema.IsolateWidgets(scratch);

        (await scratch.Audit(OwnershipMap.Current, audit => audit.TablesWithoutClass())).Should().ContainMatch("*scratch_widgets*no ownership class*");
        (await scratch.Audit(ScratchSchema.Widgets(OwnershipMap.Current), audit => audit.TablesWithoutClass())).Should().BeEmpty();
    }

    [Fact]
    public async Task A_policy_that_ignores_the_space_fails_until_the_helper_writes_it()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await ScratchSchema.CreateWidgets(scratch);
        await scratch.Run("ALTER TABLE scratch_widgets ENABLE ROW LEVEL SECURITY; ALTER TABLE scratch_widgets FORCE ROW LEVEL SECURITY");
        await scratch.Run("CREATE POLICY space_isolation ON scratch_widgets FOR ALL USING (true) WITH CHECK (true)");
        var ownership = ScratchSchema.Widgets(OwnershipMap.Current);
        await using var harness = await scratch.Harness(ownership, ScratchSchema.AllSeeders());

        (await scratch.Audit(ownership, audit => audit.RowSecurityNotForced())).Should().BeEmpty("security is on, only the policy is wrong");
        (await scratch.Audit(ownership, audit => audit.IncompletePolicies())).Should().ContainMatch("*scratch_widgets*space_id*");
        var leak = () => SpaceChecks.CannotSelectAnotherSpacesRows(harness, "scratch_widgets");
        await leak.Should().ThrowAsync<IsolationViolationException>();

        await scratch.Run("DROP POLICY space_isolation ON scratch_widgets");
        await ScratchSchema.IsolateWidgets(scratch);

        (await scratch.Audit(ownership, audit => audit.IncompletePolicies())).Should().BeEmpty();
        await using var corrected = await scratch.Harness(ownership, ScratchSchema.AllSeeders());
        await RunSpaceChecks(corrected, "scratch_widgets");
    }

    [Fact]
    public async Task A_scratch_table_with_a_nullable_space_id_fails_until_the_column_is_not_null()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await ScratchSchema.CreateWidgets(scratch, "id uuid PRIMARY KEY, space_id uuid, label text NOT NULL");
        await ScratchSchema.IsolateWidgets(scratch);
        var ownership = ScratchSchema.Widgets(OwnershipMap.Current);

        (await scratch.Audit(ownership, audit => audit.NullableDiscriminators())).Should().ContainMatch("*scratch_widgets.space_id*NOT NULL*");

        await scratch.Run("ALTER TABLE scratch_widgets ALTER COLUMN space_id SET NOT NULL");

        (await scratch.Audit(ownership, audit => audit.NullableDiscriminators())).Should().BeEmpty();
    }

    #endregion

    #region Seeders

    [Fact]
    public void A_scratch_entity_with_no_seeder_fails_until_one_is_registered()
    {
        var ownership = ScratchSchema.Widgets(OwnershipMap.Current);

        SeederRegistry.Problems(ownership, SeederRegistry.Discover()).Should().ContainMatch("*ScratchWidget*no seeder*");
        SeederRegistry.Problems(ownership, [.. SeederRegistry.Discover(), .. ScratchSchema.AllSeeders()]).Should().BeEmpty();
    }

    #endregion

    #region Foreign Keys

    [Fact]
    public async Task A_scratch_child_that_omits_the_audience_from_its_foreign_key_fails_until_it_repeats_it()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await ScratchSchema.CreateAudiencePair(scratch, ChildKey.OmitsAudience);
        var ownership = ScratchSchema.AudiencePair(OwnershipMap.Current);
        await using var harness = await scratch.Harness(ownership, ScratchSchema.AllSeeders());

        (await scratch.Audit(ownership, audit => audit.ForeignKeysWithoutAudience())).Should().ContainMatch("*scratch_child*scratch_parent*audience_user_id*");
        (await scratch.Audit(ownership, audit => audit.ForeignKeysOutsideTheSpace())).Should().BeEmpty("the key repeats the space");
        var wider = () => AudienceChecks.ChildCannotHaveAWiderAudienceThanItsParent(harness, "scratch_child");
        await wider.Should().ThrowAsync<IsolationViolationException>();

        await scratch.Run("DROP TABLE scratch_child, scratch_parent");
        await ScratchSchema.CreateAudiencePair(scratch, ChildKey.Correct);

        (await scratch.Audit(ownership, audit => audit.ForeignKeysWithoutAudience())).Should().BeEmpty();
        await using var corrected = await scratch.Harness(ownership, ScratchSchema.AllSeeders());
        await RunAudienceChecks(corrected, "scratch_child");
    }

    [Fact]
    public async Task A_scratch_child_that_omits_the_space_from_its_foreign_key_fails_until_it_repeats_it()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await ScratchSchema.CreateAudiencePair(scratch, ChildKey.OmitsSpace);
        var ownership = ScratchSchema.AudiencePair(OwnershipMap.Current);
        await using var harness = await scratch.Harness(ownership, ScratchSchema.AllSeeders());

        (await scratch.Audit(ownership, audit => audit.ForeignKeysOutsideTheSpace())).Should().ContainMatch("*scratch_child*scratch_parent*space_id*");
        var crossing = () => SpaceChecks.CannotReferenceAnotherSpacesParent(harness, "scratch_child");
        await crossing.Should().ThrowAsync<IsolationViolationException>();

        await scratch.Run("DROP TABLE scratch_child, scratch_parent");
        await ScratchSchema.CreateAudiencePair(scratch, ChildKey.Correct);

        (await scratch.Audit(ownership, audit => audit.ForeignKeysOutsideTheSpace())).Should().BeEmpty();
        await using var corrected = await scratch.Harness(ownership, ScratchSchema.AllSeeders());
        await SpaceChecks.CannotReferenceAnotherSpacesParent(corrected, "scratch_child");
    }

    #endregion

    #region The sys Schema

    [Fact]
    public async Task A_sys_function_with_no_manifest_entry_fails_until_the_manifest_lists_it()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await scratch.Run("CREATE FUNCTION sys.scratch_peek() RETURNS int LANGUAGE sql AS 'SELECT 1'");
        var listed = ManifestFile.ReadSystemFunctions();

        (await scratch.Audit(OwnershipMap.Current, audit => audit.SystemFunctionDrift(), listed))
            .Should().ContainMatch("*sys.scratch_peek()*not in SystemFunctions.manifest*");
        (await scratch.Audit(OwnershipMap.Current, audit => audit.SystemFunctionDrift(), [.. listed, "sys.scratch_peek()"]))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Changing_the_signature_of_a_listed_sys_function_fails_until_the_manifest_follows()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await scratch.Run("CREATE FUNCTION sys.scratch_peek() RETURNS int LANGUAGE sql AS 'SELECT 1'");
        var listed = ManifestFile.ReadSystemFunctions();
        await scratch.Run("DROP FUNCTION sys.scratch_peek()");
        await scratch.Run("CREATE FUNCTION sys.scratch_peek(who uuid) RETURNS int LANGUAGE sql AS 'SELECT 1'");

        var drift = await scratch.Audit(OwnershipMap.Current, audit => audit.SystemFunctionDrift(), [.. listed, "sys.scratch_peek()"]);

        drift.Should().ContainMatch("*sys.scratch_peek(uuid)*not in SystemFunctions.manifest*");
        drift.Should().ContainMatch("*sys.scratch_peek()*not in schema sys*");
        (await scratch.Audit(OwnershipMap.Current, audit => audit.SystemFunctionDrift(), [.. listed, "sys.scratch_peek(uuid)"]))
            .Should().BeEmpty();
    }

    #endregion

    #region The App Role

    [Fact]
    public async Task An_app_role_that_can_create_in_a_schema_fails_until_the_grant_is_revoked()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await scratch.RunAsAdmin("GRANT CREATE ON SCHEMA public TO drover_app");

        (await scratch.Audit(OwnershipMap.Current, audit => audit.AppRoleWeaknesses())).Should().ContainMatch("*can create in schema public*");

        await scratch.RunAsAdmin("REVOKE CREATE ON SCHEMA public FROM drover_app");

        (await scratch.Audit(OwnershipMap.Current, audit => audit.AppRoleWeaknesses())).Should().BeEmpty();
    }

    [Fact]
    public async Task An_app_role_that_owns_a_table_fails_until_ownership_is_returned()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await ScratchSchema.CreateWidgets(scratch);
        await ScratchSchema.IsolateWidgets(scratch);
        await scratch.RunAsAdmin("ALTER TABLE scratch_widgets OWNER TO drover_app");

        (await scratch.Audit(OwnershipMap.Current, audit => audit.AppRoleWeaknesses())).Should().ContainMatch("*owns*pg_class*");

        await scratch.RunAsAdmin("ALTER TABLE scratch_widgets OWNER TO drover_owner");

        (await scratch.Audit(OwnershipMap.Current, audit => audit.AppRoleWeaknesses())).Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static async Task RunSpaceChecks(IsolationHarness harness, string table)
    {
        await SpaceChecks.CannotSelectAnotherSpacesRows(harness, table);
        await SpaceChecks.CannotUpdateAnotherSpacesRows(harness, table);
        await SpaceChecks.CannotDeleteAnotherSpacesRows(harness, table);
        await SpaceChecks.CannotInsertWithAnotherSpacesId(harness, table);
        await SpaceChecks.CannotMoveARowIntoAnotherSpace(harness, table);
        await SpaceChecks.SeesNothingWithNoTenantSet(harness, table);
    }

    private static async Task RunAudienceChecks(IsolationHarness harness, string table)
    {
        await AudienceChecks.CannotSelectAnotherMembersPrivateRows(harness, table);
        await AudienceChecks.CannotUpdateAnotherMembersPrivateRows(harness, table);
        await AudienceChecks.CannotDeleteAnotherMembersPrivateRows(harness, table);
        await AudienceChecks.CannotInsertARowAddressedToAnotherMember(harness, table);
        await AudienceChecks.CannotHandARowToAnotherMember(harness, table);
        await AudienceChecks.ChildCannotHaveAWiderAudienceThanItsParent(harness, table);
        await AudienceChecks.ChildCannotBeWidenedBeyondItsParent(harness, table);
        await AudienceChecks.SharingTheParentSharesTheChild(harness, table);
    }

    #endregion
}
