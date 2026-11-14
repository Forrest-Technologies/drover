namespace Drover.Server.Health;

/// <summary>
/// The status page of a space, for its owners and admins. The space's rows come from what its connections last
/// reported; nothing here calls a provider or a tool server. The server's own rows follow the deployment rule
/// <see cref="DeploymentPolicy.SpaceAdminsSeeServerStatus"/>. The service opens its own short tenant scopes, so the
/// endpoint sits outside the tenant scope filter.
/// </summary>
public static class StatusEndpoints
{
    #region Private Fields

    private const string Server = "server";

    private const string Space = "space";

    private const string Ok = "ok";

    private const string Warning = "warning";

    private const string Problem = "problem";

    private const string Off = "off";

    #endregion

    #region Public Methods

    public static IEndpointRouteBuilder MapStatusEndpoints(this RouteGroupBuilder space)
    {
        space.MapGet("/status", GetStatus).RequireSpaceRole(MembershipRole.Admin).WithName("GetSpaceStatus")
            .WithSummary("The status page: the space's connections and, where the deployment allows, the server's own rows.");
        return space;
    }

    /// <summary>The database row: who the connection really is, and whether that role can step around row-level security.</summary>
    public static async Task<StatusRow> CheckDatabase(string connectionString, ILogger logger, CancellationToken cancellationToken)
    {
        DatabaseRole role;

        try
        {
            role = await DatabaseRole.Inspect(connectionString, cancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or IOException)
        {
            logger.LogWarning(exception, "The status page could not reach PostgreSQL");
            return Row(Server, "Database", Problem, "PostgreSQL does not answer. Check that the database is running.");
        }

        if (role.IsApplicationRole)
        {
            return Row(Server, "Database", Ok, $"Connected as {DatabaseRole.Application}, which cannot step around row-level security.");
        }

        return role.IsSuperuser || role.BypassesRowLevelSecurity
            ? Row(Server, "Database", Problem,
                $"Connected as {role.Name}, which can step around row-level security, so one space's data is not kept from another. Connect as {DatabaseRole.Application}.")
            : Row(Server, "Database", Warning, $"Connected as {role.Name}, not {DatabaseRole.Application}.");
    }

    #endregion

    #region Endpoints

    private static async Task<Ok<StatusReport>> GetStatus(
        HttpContext context,
        StatusService status,
        IOptions<DatabaseOptions> database,
        IOptions<ToolSignInOptions> signIn,
        KeyRingLocation keyRing,
        SandboxAvailability sandboxes,
        ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger(typeof(StatusEndpoints).FullName!);
        var cancellationToken = context.RequestAborted;
        List<StatusRow> rows = [];

        if (status.SpaceAdminsSeeServerStatus)
        {
            var server = await status.ReadServer(cancellationToken);
            rows.Add(await CheckDatabase(database.Value.ConnectionString, logger, cancellationToken));
            rows.Add(Migrations(server));
            rows.Add(KeyRing(keyRing.Path, logger));
            rows.Add(PublicUrl(signIn.Value.PublicUrl));
            rows.Add(Workload(server.Workload));
            rows.Add(Sandboxes(sandboxes));
        }

        var space = await status.ReadSpace(cancellationToken);
        rows.AddRange(space.Models.Select(ModelRow));
        rows.AddRange(space.Tools.Select(ToolRow));
        return TypedResults.Ok(new StatusReport(status.SpaceAdminsSeeServerStatus, rows));
    }

    #endregion

    #region Rows

    private static StatusRow Migrations(ServerReadings server) =>
        server.AppliedMigration switch
        {
            null => Row(Server, "Migrations", Problem, "No migration is applied. Run the database initialization: docker compose up does it, and elsewhere a one-off run of the image with --initialize-database."),
            var applied when applied == server.NewestKnownMigration => Row(Server, "Migrations", Ok, $"Up to date at {applied}."),
            var applied => Row(Server, "Migrations", Problem,
                $"The database is at {applied} and this build expects {server.NewestKnownMigration}. Run the database initialization: docker compose up does it, and elsewhere a one-off run of the image with --initialize-database."),
        };

    private static StatusRow KeyRing(string path, ILogger logger)
    {
        const string BackUp = "Back it up together with the database: without it, stored keys and passwords cannot be read.";

        if (!Directory.Exists(path))
        {
            return Row(Server, "Key ring", Problem, "The key ring folder is missing. Stored keys and passwords cannot be read.");
        }

        if (!IsWritable(path, logger))
        {
            return Row(Server, "Key ring", Problem, "The key ring folder cannot be written to, so no new key can be made.");
        }

        var keys = Directory.GetFiles(path, "key-*.xml").Length;
        return Row(Server, "Key ring", Ok, keys switch
        {
            0 => $"No key yet; one is made when the first key or password is stored. {BackUp}",
            1 => $"1 key, writable. {BackUp}",
            _ => $"{keys} keys, writable. {BackUp}",
        });
    }

    private static StatusRow PublicUrl(Uri? address) =>
        address switch
        {
            null => Row(Server, "Public address", Warning,
                $"Not set. Tool servers that need a sign-in cannot be connected until {PublicAddress.Variable} names the address people open Drover at."),
            { Scheme: "http", IsLoopback: false } => Row(Server, "Public address", Warning,
                $"{address.GetLeftPart(UriPartial.Authority)} uses plain HTTP. Put HTTPS in front before other people sign in over the network."),
            _ => Row(Server, "Public address", Ok, $"People open Drover at {address.GetLeftPart(UriPartial.Authority)}."),
        };

    private static StatusRow Workload(RunWorkload workload) =>
        workload.ExpiredLeases > 0
            ? Row(Server, "Background work", Warning,
                $"{workload.ExpiredLeases} running {Runs(workload.ExpiredLeases)} lost {(workload.ExpiredLeases == 1 ? "its" : "their")} worker and will be picked up again.")
            : Row(Server, "Background work", Ok,
                $"{workload.QueuedRuns} queued, {workload.RunningRuns} running, {workload.WaitingRuns} waiting for an answer.");

    /// <summary>Whether bots can have sandboxes here. Only that: never which provider, and never anything of its key.</summary>
    private static StatusRow Sandboxes(SandboxAvailability sandboxes) =>
        sandboxes.IsConfigured
            ? Row(Server, "Sandboxes", Ok, "Configured. Bots given a sandbox can run code in it.")
            : Row(Server, "Sandboxes", Off, "Not configured. Bots cannot run code until the operator sets a sandbox provider key.");

    private static StatusRow ModelRow(ModelConnection connection) =>
        connection.Status switch
        {
            ConnectionStatus.Ready => Row(Space, connection.Name, Ok, "Answered when last checked.", connection.LastCheckedAt),
            ConnectionStatus.Failed => Row(Space, connection.Name, Problem, "Did not answer when last checked. Test it again, or replace its key.", connection.LastCheckedAt),
            ConnectionStatus.Disabled => Row(Space, connection.Name, Off, "Turned off.", connection.LastCheckedAt),
            _ => Row(Space, connection.Name, Warning, "Not checked yet.", connection.LastCheckedAt),
        };

    private static StatusRow ToolRow(ToolConnection connection) =>
        connection.Status switch
        {
            ConnectionStatus.Ready => Row(Space, connection.Name, Ok, "Listed its tools when last asked.", connection.LastListedAt),
            ConnectionStatus.Failed => Row(Space, connection.Name, Problem, "Did not list its tools when last asked. See Tools for what it said.", connection.LastListedAt),
            ConnectionStatus.Disabled => Row(Space, connection.Name, Off, "Turned off.", connection.LastListedAt),
            _ => Row(Space, connection.Name, Warning, "Has not listed its tools yet.", connection.LastListedAt),
        };

    #endregion

    #region Private Methods

    private static StatusRow Row(string area, string name, string state, string summary, DateTimeOffset? checkedAt = null) =>
        new(area, name, state, summary, checkedAt);

    private static string Runs(long count) => count == 1 ? "run" : "runs";

    private static bool IsWritable(string path, ILogger logger)
    {
        try
        {
            using var probe = new FileStream(
                Path.Combine(path, $".status-{Guid.NewGuid():N}"),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "The key ring folder cannot be written to");
            return false;
        }
    }

    #endregion
}
