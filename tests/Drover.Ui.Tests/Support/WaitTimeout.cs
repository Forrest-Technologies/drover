namespace Drover.Ui.Tests.Support;

/// <summary>
/// How long every bUnit wait in this test project may take to come true.
/// </summary>
internal static class WaitTimeout
{
    #region Public Methods

    /// <summary>
    /// A screen follows the fake server's answers and live events through several renders. On a busy test machine that
    /// takes longer than bUnit's one-second default, and a wait that ran out failed with the page half way through a
    /// run's events (#171). bUnit keeps this timeout in a static property, so it is set once here, before any test
    /// runs, and applies to every bUnit test alike. Assertions are unchanged; a wait still returns as soon as its
    /// assertion holds, and only one that never comes true takes longer to fail.
    /// </summary>
    [ModuleInitializer]
    public static void Set() => BunitContext.DefaultWaitTimeout = TimeSpan.FromSeconds(10);

    #endregion
}
