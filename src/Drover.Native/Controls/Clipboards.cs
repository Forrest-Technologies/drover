namespace Drover.Native.Controls;

/// <summary>Puts text on the platform's clipboard, the one way every control that copies does it.</summary>
public static class Clipboards
{
    #region Public Methods

    /// <summary>Copies the text as it is, with its tabs and line breaks. False when the platform refused.</summary>
    public static bool Write<TCopier>(string text)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.For<TCopier>().LogWarning(exception, "The platform refused to copy text");
            return false;
        }
    }

    #endregion
}
