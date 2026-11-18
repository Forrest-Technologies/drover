namespace Drover.Native.Layouts;

/// <summary>
/// One way to a screen in the bar or the dock. Where you are is marked by weight and a line, not color alone.
/// </summary>
internal sealed class NavLink : PlainButton
{
    #region Private Fields

    private readonly string address;

    private readonly bool matchesWhole;

    private readonly bool stacked;

    private readonly TextBlock words = new();

    private readonly Icon? icon;

    private readonly Border mark = new();

    #endregion

    #region Constructors

    public NavLink(string label, string address, bool matchesWhole = false, IconName? icon = null)
    {
        (this.address, this.matchesWhole, stacked) = (address, matchesWhole, icon is not null);
        (Ink, HoverInk) = ("DvText2", "DvText");
        words.Text = label;
        MinHeight = Tokens.Number("DvTargetTouch");

        if (icon is { } name)
        {
            // The dock of a phone: the icon over the word, the whole cell pressable, a line across the top when here.
            this.icon = new() { NameOfIcon = name, Size = 20, HorizontalAlignment = HorizontalAlignment.Center };
            var column = new StackPanel { Spacing = Tokens.Number("DvSpace1"), VerticalAlignment = VerticalAlignment.Center };
            column.Children.Add(this.icon);
            column.Children.Add(words);
            var cell = new Grid();
            mark.Height = Tokens.Number("DvBorder");
            mark.VerticalAlignment = VerticalAlignment.Top;
            cell.Children.Add(column);
            cell.Children.Add(mark);
            Content = cell;
            (HoverFill, Radius, RingInside) = (null, null, true);
            MinHeight = Tokens.Number("DvTargetTouch") + Tokens.Number("DvSpace3");
            Padding = new(0);
            FontSize = Tokens.Number("DvText12");
            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            words.HorizontalAlignment = HorizontalAlignment.Center;
        }
        else
        {
            Content = words;
            Padding = new(Tokens.Number("DvSpace3"), 0, Tokens.Number("DvSpace3"), 0);
        }

        Click += (_, _) => Link.Opener?.Invoke(address);
    }

    #endregion

    #region Public Methods

    /// <summary>Marks the link when the app is at its address, or under it.</summary>
    public void ShowWhere(string current)
    {
        var path = AddressParts.Path(current);
        var here = matchesWhole ? path == address : path == address || path.StartsWith($"{address}/", StringComparison.Ordinal);

        (Ink, HoverInk) = (here ? "DvText" : "DvText2", "DvText");
        FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary[here ? "DvWeightHeading" : "DvWeightLabel"];
        words.TextDecorations = here && !stacked ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None;
        mark.Tag = here;
        AutomationProperties.SetItemStatus(this, here ? "Current page" : string.Empty);
        Repaint();
    }

    #endregion

    #region Private Methods

    protected override AutomationPeer OnCreateAutomationPeer() => new RoleButtonPeer(this, AutomationControlType.Hyperlink);

    protected override void Painted()
    {
        icon?.Foreground = CurrentInk;
        mark.Background = mark.Tag is true ? Tokens.Brush(this, "DvText") : null;
    }

    #endregion
}
