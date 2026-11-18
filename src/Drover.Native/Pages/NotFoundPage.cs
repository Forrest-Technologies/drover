namespace Drover.Native.Pages;

/// <summary>What is shown at an address that has no screen.</summary>
public sealed class NotFoundPage : Screen
{
    #region Constructors

    public NotFoundPage()
    {
        Content = new NotFound();
    }

    #endregion

    #region Properties

    public override string Title => "Not found - Drover";

    #endregion
}
