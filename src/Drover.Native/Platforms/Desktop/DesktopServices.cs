namespace Drover.Native.Platform;

/// <summary>What the app is given on a desktop beyond any device: saving and opening a conversation's files.</summary>
internal static class DesktopServices
{
    #region Public Methods

    public static IServiceCollection AddDevice(this IServiceCollection services)
    {
        services.AddHttpClient("desktop-updates");
        return services
            .AddSingleton<IFileDelivery, DesktopFileDelivery>()
            .AddSingleton<IDesktopUpdates, DesktopUpdates>();
    }

    #endregion
}
