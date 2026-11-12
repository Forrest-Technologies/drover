namespace Drover.Ui;

/// <summary>
/// Registers the services the interface kit needs.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDroverUi(this IServiceCollection services) =>
        services.AddScoped<ToastService>();
}
