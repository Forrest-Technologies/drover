namespace Drover.Data.Tests.Isolation;

/// <summary>
/// Reads <c>pg_catalog</c> and reports every way the database departs from the tenancy rules. Each check returns
/// one message per problem, so an empty list means the rule holds. The catalog tests assert that the real
/// database is clean; the scratch tests build a database that breaks one rule and assert the check finds it.
/// </summary>
public sealed class CatalogAudit(NpgsqlConnection connection, OwnershipMap ownership, IReadOnlyList<string> systemFunctions)
{
    #region Private Fields

    private const string AppRole = "drover_app";

    private const string SpaceId = "space_id";

    private const string AudienceUserId = "audience_user_id";

    private const string UserId = "user_id";

    private static readonly (string Catalog, string Column)[] ownerCatalogs =
    [
        ("pg_class", "relowner"),
        ("pg_proc", "proowner"),
        ("pg_namespace", "nspowner"),
        ("pg_type", "typowner"),
        ("pg_database", "datdba"),
    ];

    private IReadOnlyList<TableShape>? shapes;

    #endregion

    #region Ownership Classes

    /// <summary>Every table in <c>public</c> has a class, and every table the model or manifest names exists.</summary>
    public async Task<IReadOnlyList<string>> TablesWithoutClass()
    {
        var problems = new List<string>(ownership.Problems);
        var present = await Shapes();

        foreach (var shape in present.Where(shape => ownership.Find(shape.Name)?.Class is null or OwnershipClass.Unclassified))
        {
            problems.Add(string.Join(
                " ",
                $"Table '{shape.Name}' has no ownership class.",
                "Make its entity ISpaceOwned or IUserOwned, or list it in GlobalTables.manifest."));
        }

        foreach (var table in ownership.Tables.Where(table => present.All(shape => shape.Name != table.Table)))
        {
            problems.Add($"Table '{table.Table}' is in the model or in GlobalTables.manifest but not in the database.");
        }

        problems.AddRange(await GlobalTablesWithoutEntityThatTheAppRoleCanReach());
        return problems;
    }

    #endregion

    #region Row-Level Security

    /// <summary>Row-level security is on, and forced for the table owner too, on every tenant table.</summary>
    public async Task<IReadOnlyList<string>> RowSecurityNotForced()
    {
        var problems = new List<string>();

        foreach (var table in TenantTables())
        {
            var (enabled, forced) = await ReadRowSecurity(table.Table);

            if (!enabled || !forced)
            {
                problems.Add($"Table '{table.Table}' must have row-level security enabled and forced (enabled: {enabled}, forced: {forced}).");
            }
        }

        return problems;
    }

    /// <summary>Each tenant table has a policy for all commands with both <c>USING</c> and <c>WITH CHECK</c>.</summary>
    public async Task<IReadOnlyList<string>> IncompletePolicies()
    {
        var problems = new List<string>();
        var policies = await ReadPolicies();

        foreach (var table in TenantTables())
        {
            var own = policies.Where(policy => policy.Table == table.Table).ToList();

            foreach (var requirement in RequirementsFor(table.Class))
            {
                if (!own.Any(requirement.IsMetBy))
                {
                    problems.Add($"Table '{table.Table}' lacks {requirement.Description}.");
                }
            }
        }

        return problems;
    }

    #endregion

    #region Discriminator Columns

    /// <summary>The discriminator columns the class needs exist, are uuid, and do not allow null.</summary>
    public async Task<IReadOnlyList<string>> NullableDiscriminators()
    {
        var problems = new List<string>();
        var present = await Shapes();

        foreach (var table in TenantTables())
        {
            var shape = present.FirstOrDefault(found => found.Name == table.Table);

            foreach (var column in DiscriminatorsFor(table.Class))
            {
                problems.AddRange(shape is null ? [] : CheckDiscriminator(shape, column));
            }
        }

        return problems;
    }

    #endregion

    #region Foreign Keys

    /// <summary>A foreign key between two space-owned tables includes <c>space_id</c> on both sides.</summary>
    public async Task<IReadOnlyList<string>> ForeignKeysOutsideTheSpace()
    {
        var problems = new List<string>();

        foreach (var key in await SpaceToSpaceKeys())
        {
            if (!key.Includes(SpaceId))
            {
                problems.Add($"Foreign key '{key.Name}' from '{key.Table}' to '{key.ParentTable}' does not include {SpaceId}.");
            }
        }

        return problems;
    }

    /// <summary>A foreign key to a table with an audience includes <c>audience_user_id</c>, so a child cannot be wider.</summary>
    public async Task<IReadOnlyList<string>> ForeignKeysWithoutAudience()
    {
        var problems = new List<string>();
        var keys = await ReadForeignKeys();

        foreach (var key in keys.Where(key => ownership.Find(key.ParentTable)?.Class is OwnershipClass.SpaceWithAudience))
        {
            if (!key.Includes(AudienceUserId))
            {
                problems.Add($"Foreign key '{key.Name}' from '{key.Table}' to '{key.ParentTable}' does not include {AudienceUserId}.");
            }
        }

        return problems;
    }

    #endregion

    #region Roles And Functions

    /// <summary>The app role is not a superuser, bypasses nothing, owns nothing, and cannot create anything.</summary>
    public async Task<IReadOnlyList<string>> AppRoleWeaknesses()
    {
        var problems = new List<string>();
        problems.AddRange(await ReadRoleAttributeProblems());
        problems.AddRange(await ReadOwnedObjectProblems());
        problems.AddRange(await ReadCreationProblems());
        problems.AddRange(await ReadMembershipProblems());
        return problems;
    }

    /// <summary>The functions in schema <c>sys</c> are exactly the ones <c>SystemFunctions.manifest</c> lists.</summary>
    public async Task<IReadOnlyList<string>> SystemFunctionDrift()
    {
        var actual = await ReadSystemFunctions();
        var problems = new List<string>();

        problems.AddRange(actual.Except(systemFunctions).Select(signature =>
            $"Function {signature} is in schema sys but not in SystemFunctions.manifest."));
        problems.AddRange(systemFunctions.Except(actual).Select(signature =>
            $"Function {signature} is in SystemFunctions.manifest but not in schema sys."));
        return problems;
    }

    #endregion

    #region Policy Requirements

    private static IEnumerable<PolicyRequirement> RequirementsFor(OwnershipClass tableClass)
    {
        if (tableClass is OwnershipClass.Space or OwnershipClass.SpaceWithAudience)
        {
            yield return new PolicyRequirement(
                "a permissive policy for all commands matching space_id to the current space",
                true,
                SpaceId,
                "current_space()");
        }

        if (tableClass is OwnershipClass.SpaceWithAudience)
        {
            yield return new PolicyRequirement(
                "a restrictive policy for all commands limiting audience_user_id to everyone or the current user",
                false,
                AudienceUserId,
                "everyone()",
                "current_user_id()");
        }

        if (tableClass is OwnershipClass.User)
        {
            yield return new PolicyRequirement(
                "a permissive policy for all commands matching user_id to the current user",
                true,
                UserId,
                "current_user_id()");
        }
    }

    private static IEnumerable<string> DiscriminatorsFor(OwnershipClass tableClass) => tableClass switch
    {
        OwnershipClass.Space => [SpaceId],
        OwnershipClass.SpaceWithAudience => [SpaceId, AudienceUserId],
        OwnershipClass.User => [UserId],
        _ => [],
    };

    #endregion

    #region Private Methods

    private IEnumerable<TableOwnership> TenantTables() => ownership.Tables.Where(table => table.IsTenantOwned);

    private async Task<IReadOnlyList<TableShape>> Shapes() => shapes ??= await CatalogReader.Read(connection);

    private async Task<IReadOnlyList<ForeignKeySpec>> ReadForeignKeys() => await CatalogReader.ReadForeignKeys(connection);

    private async Task<IEnumerable<ForeignKeySpec>> SpaceToSpaceKeys()
    {
        var keys = await ReadForeignKeys();
        return keys.Where(key => ownership.Find(key.Table)?.IsSpaceOwned is true && ownership.Find(key.ParentTable)?.IsSpaceOwned is true);
    }

    private static IEnumerable<string> CheckDiscriminator(TableShape shape, string column)
    {
        var found = shape.Column(column);

        if (found is null)
        {
            yield return $"Table '{shape.Name}' has no column {column}.";
        }
        else if (found.Type != "uuid")
        {
            yield return $"Column {shape.Name}.{column} must be uuid, not {found.Type}.";
        }
        else if (!found.NotNull)
        {
            yield return $"Column {shape.Name}.{column} must be NOT NULL.";
        }
    }

    private async Task<(bool Enabled, bool Forced)> ReadRowSecurity(string table)
    {
        await using var command = new NpgsqlCommand(
            "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE oid = to_regclass(quote_ident('public') || '.' || quote_ident(@table))",
            connection);
        command.Parameters.AddWithValue("table", table);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? (reader.GetBoolean(0), reader.GetBoolean(1)) : (false, false);
    }

    private async Task<List<PolicySpec>> ReadPolicies()
    {
        var policies = new List<PolicySpec>();
        await using var command = new NpgsqlCommand(
            """
            SELECT c.relname, p.polname, p.polcmd, p.polpermissive,
                   pg_get_expr(p.polqual, p.polrelid), pg_get_expr(p.polwithcheck, p.polrelid)
            FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public'
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            policies.Add(new PolicySpec(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetChar(2),
                reader.GetBoolean(3),
                await reader.IsDBNullAsync(4) ? null : reader.GetString(4),
                await reader.IsDBNullAsync(5) ? null : reader.GetString(5)));
        }

        return policies;
    }

    private async Task<IReadOnlyList<string>> GlobalTablesWithoutEntityThatTheAppRoleCanReach()
    {
        var problems = new List<string>();

        foreach (var table in ownership.Tables.Where(table => table is { Class: OwnershipClass.Global, Entity: null }))
        {
            await using var command = new NpgsqlCommand(
                $"SELECT coalesce(has_table_privilege('{AppRole}', to_regclass(quote_ident('public') || '.' || quote_ident(@table)), 'SELECT, INSERT, UPDATE, DELETE'), false)",
                connection);
            command.Parameters.AddWithValue("table", table.Table);

            if ((bool)(await command.ExecuteScalarAsync())!)
            {
                problems.Add($"Global table '{table.Table}' has no entity, so the app role must have no privilege on it.");
            }
        }

        return problems;
    }

    private async Task<IReadOnlyList<string>> ReadRoleAttributeProblems()
    {
        var problems = new List<string>();
        await using var command = new NpgsqlCommand(
            "SELECT rolsuper, rolbypassrls, rolcreatedb, rolcreaterole, rolreplication FROM pg_roles WHERE rolname = @role",
            connection);
        command.Parameters.AddWithValue("role", AppRole);
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return [$"Role {AppRole} does not exist."];
        }

        var attributes = new[] { "SUPERUSER", "BYPASSRLS", "CREATEDB", "CREATEROLE", "REPLICATION" };

        for (var index = 0; index < attributes.Length; index++)
        {
            if (reader.GetBoolean(index))
            {
                problems.Add($"Role {AppRole} has {attributes[index]}.");
            }
        }

        return problems;
    }

    private async Task<IReadOnlyList<string>> ReadOwnedObjectProblems()
    {
        var problems = new List<string>();

        foreach (var (catalog, column) in ownerCatalogs)
        {
            await using var command = new NpgsqlCommand(
                $"SELECT count(*) FROM pg_catalog.{catalog} WHERE {column} = (SELECT oid FROM pg_roles WHERE rolname = @role)",
                connection);
            command.Parameters.AddWithValue("role", AppRole);
            var owned = (long)(await command.ExecuteScalarAsync())!;

            if (owned > 0)
            {
                problems.Add($"Role {AppRole} owns {owned} row(s) of {catalog}; it must own nothing.");
            }
        }

        return problems;
    }

    private async Task<IReadOnlyList<string>> ReadCreationProblems()
    {
        var problems = new List<string>();
        await using var command = new NpgsqlCommand(
            $"""
            SELECT 'schema ' || nspname FROM pg_namespace
            WHERE nspname NOT LIKE 'pg\_%' AND nspname <> 'information_schema'
              AND has_schema_privilege('{AppRole}', oid, 'CREATE')
            UNION ALL
            SELECT 'database ' || current_database() || ' (create)' WHERE has_database_privilege('{AppRole}', current_database(), 'CREATE')
            UNION ALL
            SELECT 'database ' || current_database() || ' (temporary tables)' WHERE has_database_privilege('{AppRole}', current_database(), 'TEMPORARY')
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            problems.Add($"Role {AppRole} can create in {reader.GetString(0)}.");
        }

        return problems;
    }

    private async Task<IReadOnlyList<string>> ReadMembershipProblems()
    {
        var problems = new List<string>();
        await using var command = new NpgsqlCommand(
            """
            SELECT granted.rolname FROM pg_auth_members m
            JOIN pg_roles member ON member.oid = m.member
            JOIN pg_roles granted ON granted.oid = m.roleid
            WHERE member.rolname = @role
            """,
            connection);
        command.Parameters.AddWithValue("role", AppRole);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            problems.Add($"Role {AppRole} is a member of {reader.GetString(0)}.");
        }

        return problems;
    }

    private async Task<IReadOnlyList<string>> ReadSystemFunctions()
    {
        var signatures = new List<string>();
        await using var command = new NpgsqlCommand(
            """
            SELECT 'sys.' || p.proname || '(' || array_to_string(p.proargtypes::regtype[], ',') || ')'
            FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = 'sys'
            ORDER BY 1
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            signatures.Add(ManifestFile.Normalize(reader.GetString(0)));
        }

        return signatures;
    }

    #endregion

    #region Nested Types

    private sealed record PolicySpec(string Table, string Name, char Command, bool Permissive, string? Using, string? WithCheck);

    private sealed class PolicyRequirement(string description, bool permissive, string column, params string[] functions)
    {
        public string Description { get; } = description;

        public bool IsMetBy(PolicySpec policy) =>
            policy.Command == '*'
            && policy.Permissive == permissive
            && Mentions(policy.Using)
            && Mentions(policy.WithCheck);

        private bool Mentions(string? expression) =>
            expression is not null && expression.Contains(column) && functions.All(expression.Contains);
    }

    #endregion
}
