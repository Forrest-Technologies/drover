namespace Drover.Native.Pages;

/// <summary>
/// Words that are read out when they change and are not drawn: the web's visually hidden status, for a change a
/// person can already see some other way, such as a grant that moved or a rule that was added.
/// </summary>
public sealed class HiddenStatus : TextBlock
{
    #region Constructors

    public HiddenStatus()
    {
        Opacity = 0;
        Height = 0;
        IsHitTestVisible = false;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
    }

    #endregion

    #region Public Methods

    /// <summary>Says the words, when they are new.</summary>
    public void Say(string? words)
    {
        if (Text == (words ?? string.Empty))
        {
            return;
        }

        Text = words ?? string.Empty;
        AutomationProperties.SetName(this, Text);
        if (Text.Length > 0)
        {
            FrameworkElementAutomationPeer.FromElement(this)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    #endregion
}
