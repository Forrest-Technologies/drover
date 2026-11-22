namespace Drover.Native.Pages;

/// <summary>
/// Pick how Drover looks for you, and see it before you keep it. Each theme is a single choice in one group, drawn
/// small in itself; choosing one applies it to the whole app at once, and the page offers the theme before it until
/// the person leaves. Below are the mode and whether this device uses the same theme as the person's others.
/// </summary>
public sealed class AppearancePage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly AppearanceViewModel model = Make<AppearanceViewModel>();

    private readonly List<ThemeChoiceButton> options = [];

    private bool quiet;

    #endregion

    #region Constructors

    public AppearancePage()
    {
        Content = layout;
        model.DeviceAsks(DeviceRequests());
        model.PropertyChanged += (_, _) => Refresh();
        Refresh();

        // A theme chosen here builds the page again from the new theme's tokens. The choice keeps the focus.
        Loaded += (_, _) =>
        {
            if (model.BackLabel is not null)
            {
                options.FirstOrDefault(option => option.InUse)?.TakeFocus();
            }
        };
    }

    #endregion

    #region Properties

    public override string Title => "Appearance · Drover";

    #endregion

    #region Private Methods

    protected override Task Opened(RouteMatch<Type> route) => Task.CompletedTask;

    private static List<DeviceRequest> DeviceRequests()
    {
        List<DeviceRequest> asked = [];
        if (TextSize.IsLarge)
        {
            asked.Add(DeviceRequest.LargeText);
        }

        if (Motion.IsReduced)
        {
            asked.Add(DeviceRequest.ReducedMotion);
        }

        return asked;
    }

    private void Refresh()
    {
        quiet = true;
        var body = layout.Body;
        body.Clear();
        options.Clear();
        body.Add(PageParts.Title("Appearance"));

        foreach (var suggestion in model.Suggestions)
        {
            body.Add(Styles.Text(suggestion, "DvTextSecondary"));
        }

        if (model.BackLabel is { } back)
        {
            var button = new Button { Content = back, Kind = ButtonKind.Quiet, HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => model.GoBack();
            body.Add(button);
        }

        body.Add(ThemeList());
        body.Add(ModeChoice());
        body.Add(Everywhere());
        quiet = false;
    }

    // One group of single choices across every heading, so the arrow keys move through all the themes.
    private StackPanel ThemeList()
    {
        var list = new StackPanel { Spacing = Tokens.Number("DvSpace5") };
        AutomationProperties.SetName(list, "Theme");
        List<ThemeOptionView> every = [.. model.Groups.SelectMany(group => group.Themes)];
        var tabStop = every.FirstOrDefault(option => option.InUse) ?? every.FirstOrDefault();

        foreach (var group in model.Groups)
        {
            var heading = Styles.Text(group.Heading, "DvTextH3");
            AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
            var column = PageParts.Column(heading);
            foreach (var option in group.Themes)
            {
                var button = new ThemeChoiceButton(option) { IsTabStop = option == tabStop };
                button.Click += (_, _) => model.ChooseTheme(option.Theme.Id);
                button.KeyDown += (_, key) => Move(every, option, key);
                options.Add(button);
                column.Children.Add(button);
            }

            list.Children.Add(column);
        }

        return list;
    }

    private void Move(List<ThemeOptionView> every, ThemeOptionView from, KeyRoutedEventArgs key)
    {
        var current = every.IndexOf(from);
        int? next = key.Key switch
        {
            VirtualKey.Down or VirtualKey.Right => (current + 1) % every.Count,
            VirtualKey.Up or VirtualKey.Left => (current - 1 + every.Count) % every.Count,
            VirtualKey.Home => 0,
            VirtualKey.End => every.Count - 1,
            _ => null,
        };

        if (next is { } index)
        {
            key.Handled = true;
            model.ChooseTheme(every[index].Theme.Id);
        }
    }

    private SegmentedControl ModeChoice()
    {
        var modes = new SegmentedControl { Label = "Mode", Options = model.Modes, Value = model.Mode };
        modes.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) =>
        {
            if (!quiet && modes.Value is { } mode)
            {
                model.ChooseMode(mode);
            }
        });
        return modes;
    }

    private StackPanel Everywhere()
    {
        var toggle = new Switch { Label = "Use on all my devices", About = model.EverywhereAbout, Value = model.UsesEverywhere, IsEnabled = !model.Saving };
        toggle.RegisterPropertyChangedCallback(Switch.ValueProperty, (_, _) =>
        {
            if (!quiet)
            {
                _ = model.SetEverywhere(toggle.Value);
            }
        });
        var column = PageParts.Column(toggle);
        if (model.Problem is { } problem)
        {
            column.Children.Add(new StatusNote { Kind = NoteKind.Danger, Text = problem });
        }

        return column;
    }

    #endregion

    #region Option

    /// <summary>One theme: its picture, its name and its intent, as one choice of a single-choice group.</summary>
    private sealed class ThemeChoiceButton : PlainButton
    {
        #region Constructors

        public ThemeChoiceButton(ThemeOptionView option)
        {
            InUse = option.InUse;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Padding = Tokens.Space("DvSpace3", "DvSpace3");
            Fill = option.InUse ? "DvBg3" : "DvBg1";
            HoverFill = "DvBg3";
            Line = option.InUse ? "DvEmber" : "DvText3";
            RingInside = true;
            AutomationProperties.SetName(this, option.AccessibleName);
            AutomationProperties.SetItemStatus(this, option.InUse ? "Selected" : string.Empty);

            var name = Styles.Text(option.Theme.Name, "DvTextLabel");
            var words = PageParts.Column(name, Styles.Text(option.Theme.Intent, "DvTextSecondary"));
            if (option.InUse)
            {
                words.Children.Add(Styles.Text("In use", "DvTextCaption"));
            }

            var row = new FlowPanel { Gap = Tokens.Number("DvSpace4") };
            row.Children.Add(new ThemeSample(Tokens.Number("DvColumnMin")) { Theme = option.Theme });
            row.Children.Add(words);
            Content = row;
        }

        #endregion

        #region Properties

        public bool InUse { get; }

        #endregion

        #region Private Methods

        protected override AutomationPeer OnCreateAutomationPeer() => new RoleButtonPeer(this, AutomationControlType.RadioButton);

        #endregion
    }

    #endregion
}
