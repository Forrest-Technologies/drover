namespace Drover.Presentation.ViewModels.Chat;

/// <summary>What a tool printed, in words: error output is tagged with the name of its stream, and also colored.</summary>
public static class OutputWords
{
    #region Public Methods

    /// <summary>
    /// What copying a tool's output puts on the clipboard: every piece it printed, in the order it printed them, whole
    /// and as written, with its real tabs and line breaks. The stream tags are what the screen adds, so they are left out.
    /// </summary>
    public static string CopyText(IEnumerable<ToolOutputPiece> pieces) => string.Concat(pieces.Select(piece => piece.Text));

    #endregion

    #region Fixed Words

    /// <summary>The stream a tool writes its errors to, which is also the tag its lines carry.</summary>
    public const string ErrorStream = "stderr";

    /// <summary>The copy icon's name on a block of code.</summary>
    public const string CopyCode = "Copy code";

    /// <summary>The copy icon's name on what a tool printed or returned.</summary>
    public const string CopyOutput = "Copy output";

    #endregion
}
