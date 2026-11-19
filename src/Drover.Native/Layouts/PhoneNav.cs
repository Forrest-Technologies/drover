namespace Drover.Native.Layouts;

/// <summary>The dock of a narrow window: the places a person goes most, and the account menu.</summary>
public sealed class PhoneNav : Grid
{
    #region Private Fields

    private readonly Border bar = new();

    private readonly NavLink[] links =
    [
        new("Chats", Addresses.Home, matchesWhole: true, IconName.Conversation),
        new("Projects", Addresses.Projects, icon: IconName.Attach),
        new("Bots", Addresses.Bots, icon: IconName.Crew),
        new("Tools", Addresses.Tools, icon: IconName.Settings),
    ];

    #endregion

    #region Constructors

    public PhoneNav()
    {
        var cells = new Grid();
        AutomationProperties.SetName(cells, "Drover");
        AutomationProperties.SetLandmarkType(cells, AutomationLandmarkType.Navigation);

        foreach (var link in links)
        {
            cells.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            SetColumn(link, cells.Children.Count);
            cells.Children.Add(link);
        }

        var account = new AccountMenu { Compact = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        cells.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        SetColumn(account, cells.Children.Count);
        cells.Children.Add(account);

        bar.Child = cells;
        bar.Padding = new(Tokens.Number("DvSpace2"), 0, Tokens.Number("DvSpace2"), 0);
        bar.BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0);
        Children.Add(bar);
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

    private void Paint()
    {
        bar.Background = Tokens.Brush(this, "DvBg1");
        bar.BorderBrush = Tokens.Brush(this, "DvLine");
    }

    #endregion
}
