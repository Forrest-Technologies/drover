namespace Drover.Native.Shell;

/// <summary>
/// The pieces of the native service registration that a device supplies. The test compiles the real
/// <c>AppServices.cs</c>; these stand in only so it can run without a device head.
/// </summary>
internal static class AppDiagnostics
{
    #region Properties

    public static object Log { get; } = new();

    public static object ThisApp { get; } = new();

    #endregion
}

/// <summary>Device-only registrations, left out of this build of the native service registration.</summary>
internal static class ShellTestExtensions
{
    #region Public Methods

    public static ILoggingBuilder AddPlatformLog(this ILoggingBuilder logging) => logging;

    public static ILoggingBuilder AddDiagnosticLog(this ILoggingBuilder logging, object log) => logging;

    public static IServiceCollection AddPlatform(this IServiceCollection services) => services;

    #endregion
}
