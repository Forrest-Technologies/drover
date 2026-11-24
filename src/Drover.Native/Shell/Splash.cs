namespace Drover.Native.Shell;

/// <summary>
/// What the app shows while it starts and does not know yet who is signed in: the mark alone, large, in the middle
/// of the first surface. Nothing moves and nothing is written, so the name is what assistive technology reads.
/// </summary>
public sealed class Splash : Grid
{
    #region Private Fields

    private const string Starting = "Drover is starting";

    #endregion

    #region Constructors

    public Splash()
    {
        var size = Tokens.Number("DvMarkSplash");
        Children.Add(new DroverMark
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        AutomationProperties.SetName(this, Starting);
        this.WhenShownOrRethemed(Paint);
        Paint();
    }

    #endregion

    #region Private Methods

    protected override AutomationPeer OnCreateAutomationPeer() => new RolePeer(this, AutomationControlType.Image);

    private void Paint() => Background = Tokens.Brush(this, "DvBg0");

    #endregion
}
