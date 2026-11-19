namespace Drover.Native.Platform;

/// <summary>
/// Ends this process without the native teardown that follows <see cref="Environment.Exit"/>. A check that has
/// already passed can crash inside that teardown under a virtual display, and the job then looks failed.
/// </summary>
internal static class NativeStop
{
    #region Public Methods

    public static void Now(int code)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsAndroid())
        {
            ExitImmediately(code);
        }

        Environment.Exit(code);
    }

    #endregion

    #region Private Methods

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "_exit")]
    private static extern void ExitImmediately(int code);

    #endregion
}
