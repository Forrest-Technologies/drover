namespace Drover.Ui.Chat;

/// <summary>
/// The browser's own file behind a picked file. In this client every picked file began as one: chosen in the
/// page, or staged by the shell around it.
/// </summary>
public static class BrowserFiles
{
    #region Public Methods

    public static IBrowserFile Of(IPickedFile file) =>
        file as IBrowserFile ?? throw new InvalidOperationException("A file picked in this client is a browser file.");

    #endregion
}
