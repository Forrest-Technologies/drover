namespace Drover.Native.Platform;

/// <summary>What the app is given on iOS beyond the shared device services.</summary>
internal static class IosServices
{
    public static IServiceCollection AddDevice(this IServiceCollection services) =>
        services.AddSingleton<ISecureStorage, DeviceSecrets>()
            .AddSingleton<NoAudio>(_ => new(AudioHead.Apple))
            .AddSingleton<IAudioCapture>(provider => provider.GetRequiredService<NoAudio>())
            .AddSingleton<IAudioPlayback>(provider => provider.GetRequiredService<NoAudio>());
}
