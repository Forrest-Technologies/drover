namespace Drover.Data.Tenancy;

/// <summary>
/// The only way a migration turns on row-level security, so a policy cannot be written slightly wrong by hand.
/// <c>table</c> is always a compile-time table name from a migration, never user input.
/// </summary>
public static class RowLevelSecurityExtensions
{
    #region Public Methods

    public static MigrationBuilder IsolateBySpace(this MigrationBuilder migration, string table, bool audience = false)
    {
        migration.EnableForcedSecurity(table);
        migration.Sql(
            $"""
            CREATE POLICY space_isolation ON {table} FOR ALL
              USING (space_id = sys.current_space())
              WITH CHECK (space_id = sys.current_space());
            """);

        if (audience)
        {
            migration.Sql(
                $"""
                CREATE POLICY audience ON {table} AS RESTRICTIVE FOR ALL
                  USING (audience_user_id IN (sys.everyone(), sys.current_user_id()))
                  WITH CHECK (audience_user_id IN (sys.everyone(), sys.current_user_id()));
                """);
        }

        migration.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE ON {table} TO drover_app;");
        return migration;
    }

    public static MigrationBuilder IsolateByUser(this MigrationBuilder migration, string table)
    {
        migration.EnableForcedSecurity(table);
        migration.Sql(
            $"""
            CREATE POLICY user_isolation ON {table} FOR ALL
              USING (user_id = sys.current_user_id())
              WITH CHECK (user_id = sys.current_user_id());
            """);
        migration.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE ON {table} TO drover_app;");
        return migration;
    }

    public static MigrationBuilder EnableForcedSecurity(this MigrationBuilder migration, string table)
    {
        migration.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");
        migration.Sql($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;");
        return migration;
    }

    #endregion
}
