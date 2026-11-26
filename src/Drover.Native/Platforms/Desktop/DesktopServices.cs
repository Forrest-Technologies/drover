namespace Drover.Native.Platform;

/// <summary>
/// What the app is given on a desktop beyond any device: saving and opening a conversation's files, keeping the
/// sign-in in the operating system's credential store, and a listener on this computer's loopback address that a
/// browser comes back to after a tool server's sign-in page. The listener starts the first time a sign-in asks for it.
/// </summary>
internal static class DesktopServices
{
    public static IServiceCollection AddDevice(this IServiceCollection services)
    {
        services.AddHttpClient("desktop-updates");
        return services
            .AddSingleton<IFileDelivery, DesktopFileDelivery>()
            .AddSingleton<DesktopAudio>()
            .AddSingleton<IAudioCapture>(provider => provider.GetRequiredService<DesktopAudio>())
            .AddSingleton<IAudioPlayback>(provider => provider.GetRequiredService<DesktopAudio>())
            .AddSingleton<DesktopNotificationSelections>()
            .AddSingleton<DesktopNotificationSink>()
            .AddSingleton<ILiveNotifications, DesktopNotifications>()
            .AddSingleton<ILiveNotificationStatus>(provider =>
                (DesktopNotifications)provider.GetRequiredService<ILiveNotifications>())
            .AddSingleton<ISecureStorage>(provider => new DesktopSecrets(
                SystemSecrets.Open,
                provider.GetRequiredService<ILogger<DesktopSecrets>>()))
            .AddSingleton(provider => DesktopSignInReturn.Open(
                ticket => PlatformShell.Incoming.Open(Addresses.ToolSignInReturn(ticket)),
                provider.GetRequiredService<ILogger<DesktopSignInReturn>>()))
            .AddSingleton(provider => new AppSignInReturn(provider.GetRequiredService<DesktopSignInReturn>().Address))
            .AddSingleton<IDesktopUpdates, DesktopUpdates>();
    }
}
