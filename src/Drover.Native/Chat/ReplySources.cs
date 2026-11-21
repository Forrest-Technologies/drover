namespace Drover.Native.Chat;

/// <summary>
/// The sources a reply cites, under its words: each a link with the source's title and, beside it, the site it opens.
/// A source opens outside the app. Its title came from the model's provider and is shown as plain text.
/// </summary>
public sealed class ReplySources : StackPanel
{
    #region Private Fields

    private readonly TextBlock heading = Styles.Text("Sources", "DvTextCaption");

    private readonly StackPanel links = new();

    private IReadOnlyList<SourceLink> shown = [];

    #endregion

    #region Constructors

    public ReplySources()
    {
        Spacing = Tokens.Number("DvSpace1");
        AutomationProperties.SetAccessibilityView(heading, AccessibilityView.Raw);
        AutomationProperties.SetName(this, "Sources");
        Children.Add(heading);
        Children.Add(links);
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    public IReadOnlyList<SourceLink> Sources
    {
        get => shown;
        set
        {
            if (shown.SequenceEqual(value))
            {
                return;
            }

            shown = value;
            Draw();
        }
    }

    #endregion

    #region Private Methods

    protected override AutomationPeer OnCreateAutomationPeer() => new RolePeer(this, AutomationControlType.Group);

    private void Draw()
    {
        links.Children.Clear();

        foreach (var source in shown)
        {
            links.Children.Add(Row(source));
        }

        Paint();
    }

    private static FlowPanel Row(SourceLink source)
    {
        var link = new Link { Text = source.Title, Address = source.Address, MinHeight = Targets.Minimum, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(link, ChatRows.SourceName(source));

        var site = Styles.Text(source.Site, "DvTextCaption");
        site.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(site, AccessibilityView.Raw);

        // A source without a title is named by its site already, so the site is not said twice.
        var row = new FlowPanel { Gap = Tokens.Number("DvSpace2") };
        row.Children.Add(link);
        if (source.Title != source.Site)
        {
            row.Children.Add(site);
        }

        return row;
    }

    private void Paint()
    {
        heading.Foreground = Tokens.Brush(this, "DvText3");

        foreach (var site in links.Children.OfType<FlowPanel>().SelectMany(row => row.Children.OfType<TextBlock>()))
        {
            site.Foreground = Tokens.Brush(this, "DvText3");
        }
    }

    #endregion
}
