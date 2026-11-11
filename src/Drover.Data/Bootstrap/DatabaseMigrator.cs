namespace Drover.Data.Bootstrap;

public static class DatabaseMigrator
{
    #region Public Methods

    /// <summary>
    /// Applies pending migrations as <c>drover_owner</c>. This context has no tenant interceptors, because
    /// migrations are the one place that runs outside a tenant. Never use it to serve a request.
    /// </summary>
    public static async Task Run(string ownerConnectionString, CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<DroverDbContext>();
        options.UseDroverNpgsql(ownerConnectionString);
        await using var context = new DroverDbContext(options.Options);
        await context.Database.MigrateAsync(cancellationToken);
    }

    #endregion
}
