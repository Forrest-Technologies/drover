namespace Drover.Data.Bootstrap;

/// <summary>The database role a connection really runs as, and whether that role can step around row-level security.</summary>
public sealed record DatabaseRole(string Name, bool IsSuperuser, bool BypassesRowLevelSecurity)
{
    /// <summary>The role every request and job must use, and nothing else.</summary>
    public const string Application = "drover_app";

    public bool IsApplicationRole => Name == Application && !IsSuperuser && !BypassesRowLevelSecurity;

    /// <summary>Asks the database who the connection is, instead of trusting what the connection string says.</summary>
    public static async Task<DatabaseRole> Inspect(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT current_user::text, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return new DatabaseRole(string.Empty, IsSuperuser: true, BypassesRowLevelSecurity: true);
        }

        return new DatabaseRole(reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }
}
