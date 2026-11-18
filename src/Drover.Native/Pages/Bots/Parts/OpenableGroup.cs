namespace Drover.Native.Pages;

/// <summary>
/// A group under a line that opens and closes it, the web's details and summary: a source of tools, or what a tool
/// said when it was last reviewed. The line holds the group's name and, at its end, a fact about it.
/// </summary>
public sealed class OpenableGroup : StackPanel
{
    #region Private Fields

    private readonly PlainButton summary = new() { HoverFill = null, RingInside = true, Radius = null };

    private readonly TextBlock name = Styles.Text(null);

    private readonly TextBlock fact = BotParts.Mono();

    private readonly StackPanel body = new() { Spacing = Tokens.Number("DvSpace2") };

    private bool open;

    #endregion

    #region Constructors

    public OpenableGroup(bool open, bool ruled = true)
    {
        var line = new Grid { ColumnSpacing = Tokens.Number("DvSpace3") };
        line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        name.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        name.VerticalAlignment = fact.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(fact, 1);
        line.Children.Add(name);
        line.Children.Add(fact);
        summary.Content = line;
        summary.MinHeight = Tokens.Number("DvTargetTouch");
        summary.Padding = new(0);
        summary.HorizontalAlignment = HorizontalAlignment.Stretch;
        summary.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        summary.Click += (_, _) => Open = !Open;
        Children.Add(summary);
        Children.Add(body);

        if (ruled)
        {
            BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0);
            this.WhenShownOrRethemed(() => BorderBrush = Tokens.Brush(this, "DvLine"));
        }

        Open = open;
    }

    #endregion

    #region Properties

    /// <summary>What is shown while the group is open.</summary>
    public UIElementCollection Body => body.Children;

    public bool Open
    {
        get => open;
        set
        {
            open = value;
            body.Shown(open);
            Describe();
        }
    }

    #endregion

    #region Public Methods

    public void Show(string title, string? detail)
    {
        name.Show(title);
        fact.Show(detail);
        fact.Shown(detail is not null);
        Describe();
    }

    #endregion

    #region Private Methods

    private void Describe()
    {
        AutomationProperties.SetName(summary, fact.Text.Length > 0 ? $"{name.Text}, {fact.Text}" : name.Text);
        AutomationProperties.SetItemStatus(summary, open ? "Expanded" : "Collapsed");
    }

    #endregion
}
