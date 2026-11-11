namespace Drover.Data.Tenancy;

/// <summary>
/// The only way a migration creates a function in schema <c>sys</c>, so the rules of
/// docs/architecture/04-tenancy-and-permissions.md hold for every one: owned by <c>drover_system</c>,
/// <c>SECURITY DEFINER</c> with the search path pinned, <c>EXECUTE</c> revoked from everyone and granted to
/// <c>drover_app</c>. The body is also responsible for writing a row to <c>sys.audit</c>, and the function must be
/// listed in <c>SystemFunctions.manifest</c> or the data tests fail. Names and bodies are compile-time text from a
/// migration, never user input.
/// </summary>
public static class SystemFunctionExtensions
{
    #region Public Methods

    public static MigrationBuilder CreateSystemFunction(
        this MigrationBuilder migration,
        string name,
        (string Name, string Type)[] parameters,
        string returns,
        string body)
    {
        var declared = string.Join(", ", parameters.Select(parameter => $"{parameter.Name} {parameter.Type}"));
        var types = string.Join(", ", parameters.Select(parameter => parameter.Type));

        // drover_system may own a function only where it may create one, so it holds CREATE on sys for this long.
        migration.Sql("GRANT CREATE ON SCHEMA sys TO drover_system;");
        migration.Sql(
            $"""
            CREATE FUNCTION sys.{name}({declared})
            RETURNS {returns}
            LANGUAGE plpgsql
            SECURITY DEFINER
            SET search_path = pg_catalog, public
            AS $function$
            {body}
            $function$;
            """);
        migration.Sql($"ALTER FUNCTION sys.{name}({types}) OWNER TO drover_system;");
        migration.Sql("REVOKE CREATE ON SCHEMA sys FROM drover_system;");
        migration.Sql($"REVOKE ALL ON FUNCTION sys.{name}({types}) FROM PUBLIC;");
        migration.Sql($"GRANT EXECUTE ON FUNCTION sys.{name}({types}) TO drover_app;");
        return migration;
    }

    public static MigrationBuilder DropSystemFunction(this MigrationBuilder migration, string name, string types)
    {
        migration.Sql($"DROP FUNCTION sys.{name}({types});");
        return migration;
    }

    #endregion
}
