namespace Drover.Native.Theme;

/// <summary>
/// How large a control must be to be hit: 44 for a finger, 32 for a pointer. The web client asks the browser
/// whether the pointer is coarse; here the platform says so once, at startup.
/// </summary>
public static class Targets
{
    #region Properties

    /// <summary>True where people use a finger: a phone, a tablet, or a browser that reports a coarse pointer.</summary>
    public static bool IsCoarse { get; set; } = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

    /// <summary>The smallest height, and for an icon the smallest width, of a control.</summary>
    public static double Minimum => Tokens.Number(IsCoarse ? "DvTargetTouch" : "DvTargetPointer");

    #endregion
}
