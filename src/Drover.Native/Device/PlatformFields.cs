namespace Drover.Native.Platform;

/// <summary>
/// On a device a field carries its own hints: its input scope, and on Android a real field for the autofill service.
/// Nothing outside the field is told.
/// </summary>
internal static class PlatformFields
{
    #region Public Methods

    public static void Name(string label, string? autofill)
    {
    }

    #endregion
}
