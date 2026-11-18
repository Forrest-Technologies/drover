namespace Drover.Native.Controls;

/// <summary>
/// The logger of a control. A control is made by the page that shows it, not by the service provider, so it cannot
/// be handed a logger; the app gives this the logger factory once, at startup.
/// </summary>
public static class Log
{
    #region Properties

    public static ILoggerFactory Factory { get; set; } = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;

    #endregion

    #region Public Methods

    public static ILogger<T> For<T>() => Factory.CreateLogger<T>();

    #endregion
}
