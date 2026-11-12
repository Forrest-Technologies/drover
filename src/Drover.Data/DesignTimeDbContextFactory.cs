namespace Drover.Data;

/// <summary>Lets <c>dotnet ef migrations add</c> build the model. It never connects, so the string is a placeholder.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DroverDbContext>
{
    #region Public Methods

    public DroverDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DroverDbContext>();
        options.UseDroverNpgsql("Host=localhost;Database=drover_design_time");
        return new DroverDbContext(options.Options);
    }

    #endregion
}
