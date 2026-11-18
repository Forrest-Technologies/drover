namespace Drover.Native.Chat;

/// <summary>
/// Words that are read out and not shown: what a streaming reply just said, that a file finished uploading. The
/// element is always there and takes no room, since a live region is announced only if it existed before its words
/// arrived.
/// </summary>
internal sealed class SpokenWords : Grid
{
    #region Private Fields

    private readonly TextBlock words = new() { Opacity = 0, IsHitTestVisible = false };

    #endregion

    #region Constructors

    public SpokenWords()
    {
        Height = 0;
        IsHitTestVisible = false;
        AutomationProperties.SetLiveSetting(words, AutomationLiveSetting.Polite);
        Children.Add(words);
    }

    #endregion

    #region Public Methods

    /// <summary>Says the words, once: the same words again are not repeated.</summary>
    public void Say(string? text)
    {
        if ((text ?? string.Empty) == words.Text)
        {
            return;
        }

        words.Text = text ?? string.Empty;

        if (words.Text.Length > 0)
        {
            FrameworkElementAutomationPeer.FromElement(words)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    #endregion
}
