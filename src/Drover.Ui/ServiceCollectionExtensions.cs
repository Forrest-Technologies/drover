namespace Drover.Ui;

/// <summary>
/// Registers the services the interface needs. The host registers <see cref="DroverApi"/> itself, because
/// how requests reach the server differs between the web and the native shell.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDroverUi(this IServiceCollection services) =>
        services
            .AddScoped<ToastService>()
            .AddScoped<SessionState>()
            .AddScoped<ThemeService>();
}
