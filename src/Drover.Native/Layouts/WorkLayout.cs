namespace Drover.Native.Layouts;

/// <summary>
/// A screen of the workspace that is not the conversation: one column on the yard, under a plain bar. The bar names
/// the person, so nothing of it is drawn until it is known who is signed in. A narrow window drops the bar's links
/// and its account menu into a dock at the bottom, and the column scrolls between the two.
/// </summary>
[Microsoft.UI.Xaml.Markup.ContentProperty(Name = nameof(Body))]
public sealed class WorkLayout : Grid
{
    #region Private Fields

    private readonly Border header = new();

    private readonly StackPanel nav = new() { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace1") };

    private readonly AccountMenu account = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly StackPanel body = new() { Spacing = Tokens.Number("DvSpace5") };

    private readonly PhoneNav dock = new();

    private readonly NavLink[] links =
    [
        new("Conversations", Addresses.Home, matchesWhole: true),
        new("Projects", Addresses.Projects),
        new("Bots", Addresses.Bots),
        new("Models", Addresses.Models),
        new("Tools", Addresses.Tools),
        new("Extensions", Addresses.Extensions),
    ];

    private readonly Navigator navigator = ((App)Application.Current).Services.GetRequiredService<Navigator>();

    #endregion

    #region Constructors

    public WorkLayout()
    {
        var page = new Grid();
        page.RowDefinitions.Add(new() { Height = GridLength.Auto });
        page.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new() { Height = GridLength.Auto });

        AutomationProperties.SetName(nav, "Drover");
        AutomationProperties.SetLandmarkType(nav, AutomationLandmarkType.Navigation);
        foreach (var link in links)
        {
            nav.Children.Add(link);
        }

        var bar = new Grid { ColumnSpacing = Tokens.Number("DvSpace4") };
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        SetColumn(nav, 1);
        SetColumn(account, 2);
        bar.Children.Add(Home());
        bar.Children.Add(nav);
        bar.Children.Add(account);
        header.Child = bar;
        header.MinHeight = Tokens.Number("DvTargetTouch") + Tokens.Number("DvSpace3");
        header.Padding = Tokens.Space("DvSpace4", "DvSpace2");
        header.BorderThickness = new(0, 0, 0, Tokens.Number("DvBorder"));

        body.MaxWidth = Tokens.Number("DvWorkWidth");
        body.Padding = new(Tokens.Number("DvSpace4"), Tokens.Number("DvSpace5"), Tokens.Number("DvSpace4"), Tokens.Number("DvSpace7"));
        var main = new Grid();
        main.Children.Add(new Yard());
        main.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsTabStop = false });
        AutomationProperties.SetLandmarkType(main, AutomationLandmarkType.Main);

        SetRow(main, 1);
        SetRow(dock, 2);
        page.Children.Add(header);
        page.Children.Add(main);
        page.Children.Add(dock);

        var gate = new SessionGate();
        gate.Body = page;
        Children.Add(gate);

        SizeChanged += (_, _) => Fit();
        Loaded += (_, _) => Watch();
        Unloaded += (_, _) => navigator.Navigated -= ShowWhere;
        this.WhenShownOrRethemed(Paint);
        Fit();
    }

    #endregion

    #region Properties

    /// <summary>What the screen holds, stacked in the column.</summary>
    public UIElementCollection Body => body.Children;

    #endregion

    #region Private Methods

    private PlainButton Home()
    {
        var word = Styles.Text("Drover", "DvTextH3");
        word.CharacterSpacing = (int)Tokens.Dictionary["DvTracking"];
        word.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetHeadingLevel(word, AutomationHeadingLevel.None);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        content.Children.Add(new DroverMark { VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(word);

        var home = new PlainButton { Content = content, HoverFill = null, Padding = new(0), MinHeight = Tokens.Number("DvTargetTouch") };
        AutomationProperties.SetName(home, "Drover");
        home.Click += (_, _) => navigator.NavigateTo(Addresses.Home);
        return home;
    }

    private void Watch()
    {
        navigator.Navigated -= ShowWhere;
        navigator.Navigated += ShowWhere;
        ShowWhere(navigator.Current, NavigationKind.Replace);
    }

    private void ShowWhere(string address, NavigationKind kind)
    {
        foreach (var link in links)
        {
            link.ShowWhere(address);
        }

        dock.ShowWhere(address);
    }

    private void Fit()
    {
        var phone = ActualWidth > 0 && ActualWidth < (double)Styles.Dictionary["DvBreakPhone"];
        nav.Visibility = account.Visibility = phone ? Visibility.Collapsed : Visibility.Visible;
        dock.Visibility = phone ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Paint()
    {
        header.Background = Tokens.Brush(this, "DvBg1");
        header.BorderBrush = Tokens.Brush(this, "DvLine");
    }

    #endregion
}
