namespace Drover.Native.Theme;

/// <summary>Whether the person asked the device for larger text. Set once by the platform at startup.</summary>
public static class TextSize
{
    #region Private Fields

    /// <summary>A text scale above this is a request for larger text, not a rounding of the default.</summary>
    private const double LargeFrom = 1.15;

    #endregion

    #region Properties

    public static bool IsLarge { get; private set; }

    #endregion

    #region Public Methods

    public static void Read(double scale) => IsLarge = scale > LargeFrom;

    #endregion
}
