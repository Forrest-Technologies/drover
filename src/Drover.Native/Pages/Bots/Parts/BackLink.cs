namespace Drover.Native.Pages;

/// <summary>
/// The way back to the screen above, at the top of a screen: an arrow and the screen's name, quiet until the pointer
/// is over it. A link to assistive technology, followed through the navigator.
/// </summary>
public sealed class BackLink : PlainButton
{
    #region Constructors

    public BackLink(string text, string address)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        content.Children.Add(new Icon { NameOfIcon = IconName.ArrowLeft, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        Content = content;
        (Ink, HoverInk, HoverFill) = ("DvText2", "DvText", "DvBg3");
        MinHeight = Tokens.Number("DvTargetTouch");
        Padding = new(Tokens.Number("DvSpace2"), 0, Tokens.Number("DvSpace2"), 0);
        HorizontalAlignment = HorizontalAlignment.Left;
        FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        AutomationProperties.SetName(this, text);
        Click += (_, _) => ((App)Application.Current).Services.GetRequiredService<Navigator>().NavigateTo(address);
    }

    #endregion

    #region Private Methods

    protected override AutomationPeer OnCreateAutomationPeer() => new RoleButtonPeer(this, AutomationControlType.Hyperlink);

    #endregion
}
