namespace Drover.Native.Platform;

/// <summary>What the app is given on iOS beyond the shared device services.</summary>
internal static class IosServices
{
    public static IServiceCollection AddDevice(this IServiceCollection services) => services;
}
