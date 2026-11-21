namespace Drover.Ui.Chat;

/// <summary>
/// A conversation file the device could not save or share, with the reason as a sentence the file card shows. The
/// platform writes the sentence: a fixed one, never the text of a system error, which can name a path.
/// </summary>
public sealed class FileDeliveryException(string reason, Exception? inner = null) : IOException(reason, inner)
{
    #region Properties

    public string Reason => Message;

    #endregion
}
