namespace Drover.Native.Platform;

/// <summary>What the app is given on a desktop beyond any device: saving and opening a conversation's files.</summary>
internal static class DesktopServices
{
    public static IServiceCollection AddDevice(this IServiceCollection services) =>
        services.AddSingleton<IFileDelivery, DesktopFileDelivery>();
}
