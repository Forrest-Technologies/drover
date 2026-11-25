namespace Drover.Native.Layouts;

/// <summary>The dock of a narrow window: the places a person goes most, and the account menu.</summary>
public sealed class PhoneNav : Grid
{
    #region Private Fields

    private readonly Border bar = new();

    private readonly Grid cells = new();

    private readonly AccountMenu account = new() { Compact = true, HorizontalAlignment = HorizontalAlignment.Stretch };

    private (Point Cells, Point Account)? told;

    private readonly NavLink[] links =
    [
        new("Chats", Addresses.Home, matchesWhole: true, IconName.Conversation),
        new("Projects", Addresses.Projects, icon: IconName.Project),
        new("Bots", Addresses.Bots, icon: IconName.Crew),
        new("Tools", Addresses.Tools, icon: IconName.Tool),
    ];

    #endregion

    #region Constructors

    public PhoneNav()
    {
        AutomationProperties.SetName(cells, "Drover");
        AutomationProperties.SetLandmarkType(cells, AutomationLandmarkType.Navigation);

        foreach (var link in links)
        {
            cells.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            SetColumn(link, cells.Children.Count);
            cells.Children.Add(link);
        }

        cells.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        SetColumn(account, cells.Children.Count);
        cells.Children.Add(account);

        bar.Child = cells;
        bar.Padding = new(Tokens.Number("DvSpace2"), 0, Tokens.Number("DvSpace2"), 0);
        bar.BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0);
        Children.Add(bar);
        LayoutUpdated += (_, _) => TellWhereItStands();
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Public Methods

    public void ShowWhere(string current)
    {
        foreach (var link in links)
        {
            link.ShowWhere(current);
        }
    }

    #endregion

    #region Private Methods

    // The landmark and the account button stand under parts that are nothing to a screen reader: the dock itself,
    // which the screen puts at its foot, and the menu, which stands in the last cell. The browser's tree for
    // assistive technology places neither again when only those move (see SemanticTree), so once a layout has
    // moved them the bar is put back in that tree.
    private void TellWhereItStands()
    {
        if (!IsLoaded || ActualWidth <= 0)
        {
            told = null;
            return;
        }

        var stands = (Corner(cells), Corner(account));
        if (stands == told)
        {
            return;
        }

        told = stands;
        SemanticTree.PutBack(bar);
    }

    private static Point Corner(UIElement part) => part.TransformToVisual(null).TransformPoint(default);

    private void Paint()
    {
        bar.Background = Tokens.Brush(this, "DvBg1");
        bar.BorderBrush = Tokens.Brush(this, "DvLine");
    }

    #endregion
}
