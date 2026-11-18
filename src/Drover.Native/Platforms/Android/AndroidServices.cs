namespace Drover.Native.Droid;

/// <summary>
/// What the app is given on Android beyond any device: notifications over UnifiedPush, files saved and shared out,
/// shares taken in, and the scheme a tool server's sign-in comes back to. The classes are the Android shell's own,
/// shared (src/Shared/Android).
/// </summary>
internal static class AndroidServices
{
    #region Public Methods

    public static IServiceCollection AddDevice(this IServiceCollection services) =>
        services
            .AddSingleton<DocumentDestination>()
            .AddSingleton<IFileDelivery, AndroidFileDelivery>()
            .AddSingleton<AndroidShares>()
            .AddSingleton(new AppSignInReturn(MainActivity.SignInReturnScheme))
            .AddSingleton<AndroidPushRegistration>()
            .AddSingleton<IPushRegistration>(provider => provider.GetRequiredService<AndroidPushRegistration>())
            .AddScoped<AndroidPushMessages>();

    #endregion
}
