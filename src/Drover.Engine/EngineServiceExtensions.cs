namespace Drover.Engine;

public static class EngineServiceExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers Identity on Drover's own tables and the services that use it. Passwords use Identity's default
    /// hasher; an account locks after five wrong passwords for five minutes.
    /// </summary>
    public static IServiceCollection AddDroverEngine(this IServiceCollection services)
    {
        services
            .AddIdentityCore<User>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddDroverStores();
        services.AddScoped<AccountService>();
        services.AddScoped<SpaceService>();
        return services;
    }

    #endregion
}
