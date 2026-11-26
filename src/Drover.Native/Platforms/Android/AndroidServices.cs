namespace Drover.Native.Droid;

/// <summary>
/// What the app is given on Android beyond any device: the sign-in kept in the platform's credential store,
/// live streams that close while the app is out of sight, notifications over UnifiedPush, files saved and shared out, shares taken in, and the scheme a tool server's sign-in
/// comes back to. Apart from the credential store, the classes are the Android shell's own, shared
/// (src/Shared/Android).
/// </summary>
internal static class AndroidServices
{
    #region Public Methods

    public static IServiceCollection AddDevice(this IServiceCollection services) =>
        services
            .AddSingleton<ISecureStorage, DeviceSecrets>()
            .AddSingleton<AndroidAudio>()
            .AddSingleton<IAudioCapture>(provider => provider.GetRequiredService<AndroidAudio>())
            .AddSingleton<IAudioPlayback>(provider => provider.GetRequiredService<AndroidAudio>())
            .AddSingleton(MainActivity.Streams)
            .AddSingleton<DocumentDestination>()
            .AddSingleton<IFileDelivery, AndroidFileDelivery>()
            .AddSingleton<AndroidShares>()
            .AddSingleton(new AppSignInReturn(MainActivity.SignInReturnScheme))
            .AddSingleton<AndroidPushRegistration>()
            .AddSingleton<IPushRegistration>(provider => provider.GetRequiredService<AndroidPushRegistration>())
            .AddScoped<AndroidPushMessages>();

    #endregion
}
