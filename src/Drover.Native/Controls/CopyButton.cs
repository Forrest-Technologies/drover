namespace Drover.Native.Controls;

/// <summary>
/// The copy icon as a button. Pressed, it puts <see cref="Text"/> on the clipboard, turns into a check for a moment,
/// and says "Copied", so the confirmation is heard as well as seen. A refusal is said in words and the icon stays.
/// </summary>
public sealed class CopyButton : Grid
{
    #region Private Fields

    private static readonly TimeSpan confirmationTime = TimeSpan.FromSeconds(2);

    private readonly IconButton button = new() { Icon = IconName.Copy };

    // Read out and never shown. It is always there, since a live region is announced only if it existed before its words.
    private readonly TextBlock said = new() { Opacity = 0, IsHitTestVisible = false, Width = 0, Height = 0 };

    private int copies;

    #endregion

    #region Constructors

    public CopyButton()
    {
        AutomationProperties.SetLiveSetting(said, AutomationLiveSetting.Polite);
        button.Click += async (_, _) => await Copy();
        Children.Add(said);
        Children.Add(button);
    }

    #endregion

    #region Properties

    /// <summary>What the button copies, read when it is pressed.</summary>
    public Func<string>? Text { get; set; }

    /// <summary>What the button does, in words: "Copy reply", "Copy code". Its accessible name and its tooltip.</summary>
    public string Label
    {
        get => button.Label;
        set => button.Label = value;
    }

    /// <summary>The color of the icon at rest, as a token name.</summary>
    public string Ink
    {
        get => button.Ink;
        set => button.Ink = value;
    }

    #endregion

    #region Public Methods

    public void TakeFocus() => button.TakeFocus();

    #endregion

    #region Private Methods

    private async Task Copy()
    {
        if (Text is not { } read)
        {
            return;
        }

        var number = ++copies;
        var copied = Clipboards.Write<CopyButton>(read());
        button.Icon = copied ? IconName.Check : IconName.Copy;
        Say(copied ? Copied : CopyFailed);

        await Task.Delay(confirmationTime);
        if (number == copies)
        {
            button.Icon = IconName.Copy;
            said.Text = string.Empty;
        }
    }

    private void Say(string words)
    {
        said.Text = words;
        FrameworkElementAutomationPeer.FromElement(said)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    #endregion

    #region Fixed Words

    public const string Copied = "Copied";

    public const string CopyFailed = "Copy failed. Select the text and copy it by hand.";

    #endregion
}
