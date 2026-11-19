namespace Drover.Native.Platform;

/// <summary>
/// What the app is given on a desktop beyond any device: saving and opening a conversation's files, and keeping the
/// sign-in in the operating system's credential store.
/// </summary>
internal static class DesktopServices
{
    public static IServiceCollection AddDevice(this IServiceCollection services) =>
        services
            .AddSingleton<IFileDelivery, DesktopFileDelivery>()
            .AddSingleton<ISecureStorage>(provider => new DesktopSecrets(
                SystemSecrets.Open,
                provider.GetRequiredService<ILogger<DesktopSecrets>>()));
}
