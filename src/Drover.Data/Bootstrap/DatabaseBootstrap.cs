namespace Drover.Data.Bootstrap;

/// <summary>
/// Creates the three database roles and the schema-level grants they need. Run once per start, before the
/// migrations, over a connection that may create roles and grant <c>BYPASSRLS</c> (a superuser). Safe to run
/// again and from several servers at once: it holds an advisory lock and every step is idempotent.
/// </summary>
public static class DatabaseBootstrap
{
    #region Private Fields

    private const long AdvisoryLockKey = 0x44524F56_45520001;

    private const string RolesSql =
        """
        DO $$ BEGIN
          CREATE ROLE drover_owner LOGIN;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;
        DO $$ BEGIN
          CREATE ROLE drover_app LOGIN;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;
        DO $$ BEGIN
          CREATE ROLE drover_system NOLOGIN;
        EXCEPTION WHEN duplicate_object THEN NULL; END $$;

        ALTER ROLE drover_owner LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
        ALTER ROLE drover_app LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
        ALTER ROLE drover_system NOLOGIN NOSUPERUSER BYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;

        GRANT drover_system TO drover_owner;
        """;

    private const string DatabaseSql =
        """
        DO $$ BEGIN
          EXECUTE format('REVOKE ALL ON DATABASE %I FROM PUBLIC', current_database());
          EXECUTE format('GRANT CONNECT ON DATABASE %I TO drover_app', current_database());
          EXECUTE format('GRANT CONNECT, TEMPORARY ON DATABASE %I TO drover_owner', current_database());
        END $$;

        REVOKE ALL ON SCHEMA public FROM PUBLIC;
        GRANT USAGE, CREATE ON SCHEMA public TO drover_owner;
        GRANT USAGE ON SCHEMA public TO drover_app;

        CREATE SCHEMA IF NOT EXISTS sys AUTHORIZATION drover_owner;
        ALTER SCHEMA sys OWNER TO drover_owner;
        REVOKE ALL ON SCHEMA sys FROM PUBLIC;
        GRANT USAGE ON SCHEMA sys TO drover_app, drover_system;
        """;

    #endregion

    #region Public Methods

    /// <summary>
    /// <paramref name="adminConnectionString"/> is a superuser connection to the Drover database.
    /// <paramref name="ownerPassword"/> belongs to <c>drover_owner</c>, used only by the migrator, and
    /// <paramref name="appPassword"/> to <c>drover_app</c>, used by every request and job.
    /// </summary>
    public static async Task Run(
        string adminConnectionString,
        string ownerPassword,
        string appPassword,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);
        await Execute(connection, "SELECT pg_advisory_lock(@key)", cancellationToken, ("key", AdvisoryLockKey));

        try
        {
            await Execute(connection, RolesSql, cancellationToken);
            await SetPassword(connection, "drover_owner", ownerPassword, cancellationToken);
            await SetPassword(connection, "drover_app", appPassword, cancellationToken);
            await Execute(connection, DatabaseSql, cancellationToken);
        }
        finally
        {
            await Execute(connection, "SELECT pg_advisory_unlock(@key)", CancellationToken.None, ("key", AdvisoryLockKey));
        }
    }

    #endregion

    #region Private Methods

    private static async Task SetPassword(
        NpgsqlConnection connection,
        string role,
        string password,
        CancellationToken cancellationToken)
    {
        // CREATE and ALTER ROLE take no parameters, so the server builds the statement and quotes the value.
        await using var build = new NpgsqlCommand("SELECT format('ALTER ROLE %I PASSWORD %L', @role, @password)", connection);
        build.Parameters.AddWithValue("role", role);
        build.Parameters.AddWithValue("password", password);
        var statement = (string)(await build.ExecuteScalarAsync(cancellationToken))!;
        await Execute(connection, statement, cancellationToken);
    }

    private static async Task Execute(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    #endregion
}
