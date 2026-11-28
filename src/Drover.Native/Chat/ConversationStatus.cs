namespace Drover.Native.Chat;

/// <summary>Application chrome for the current model, run state, and usage of the last finished reply.</summary>
public sealed class ConversationStatus : Border
{
    #region Private Fields

    private readonly TextBlock current = Text();

    private readonly TextBlock modelWords = Text();

    private readonly TextBlock activity = Text();

    private readonly Icon expand = new()
    {
        NameOfIcon = IconName.ChevronDown,
        Size = (int)Tokens.Number("DvIcon16"),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly PlainButton modelButton = Segment();

    private readonly PlainButton usageButton = Segment();

    private readonly StackPanel figures = new() { Orientation = Orientation.Horizontal };

    private readonly UsagePopover popover = new();

    private ConversationStatusViewModel? model;

    private ComposerViewModel? composer;

    #endregion

    #region Constructors

    public ConversationStatus()
    {
        MinHeight = Targets.Minimum;
        BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0);
        AutomationProperties.SetAutomationId(this, "ConversationStatus");

        var chrome = new Grid();
        chrome.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        chrome.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        chrome.ColumnDefinitions.Add(new() { Width = GridLength.Auto });

        var name = new Grid { ColumnSpacing = Tokens.Number("DvSpace1") };
        name.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        name.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumn(expand, 1);
        name.Children.Add(modelWords);
        name.Children.Add(expand);
        modelButton.Padding = Tokens.Space("DvSpace2", "DvSpace1");
        modelButton.Content = name;

        current.Margin = Tokens.Space("DvSpace3", "DvSpace1");
        activity.Margin = Tokens.Space("DvSpace2", "DvSpace1");
        Grid.SetColumn(activity, 1);
        Grid.SetColumn(usageButton, 2);
        usageButton.Padding = new(0);
        usageButton.Content = figures;
        chrome.Children.Add(current);
        chrome.Children.Add(modelButton);
        chrome.Children.Add(activity);
        chrome.Children.Add(usageButton);
        Child = chrome;

        modelButton.Click += (_, _) =>
        {
            if (composer is { } shown)
            {
                Picker?.Open(modelButton, shown);
            }
        };
        usageButton.Click += async (_, _) => await popover.Open(usageButton);

        this.WhenShownOrRethemed(Show);
        Show();
    }

    #endregion

    #region Properties

    internal ConversationModelPicker? Picker { get; init; }

    public ConversationStatusViewModel? Model
    {
        get => model;
        set
        {
            model?.PropertyChanged -= Changed;
            model = value;
            model?.PropertyChanged += Changed;
            Show();
        }
    }

    public ComposerViewModel? Composer
    {
        get => composer;
        set
        {
            composer?.PropertyChanged -= Changed;
            composer = value;
            composer?.PropertyChanged += Changed;
            Show();
        }
    }

    #endregion

    #region Private Methods

    private static TextBlock Text() => new()
    {
        FontFamily = Tokens.FontMono,
        FontSize = Tokens.Number("DvText12"),
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static PlainButton Segment() => new()
    {
        Ink = "DvText2",
        Radius = null,
        RingInside = true,
        MinHeight = Targets.Minimum,
        Padding = Tokens.Space("DvSpace3", "DvSpace1"),
        HorizontalContentAlignment = HorizontalAlignment.Left,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            Show();
        }
        else
        {
            DispatcherQueue.TryEnqueue(Show);
        }
    }

    private void Show()
    {
        var parts = model?.Parts ?? [];
        var name = composer?.ActiveModelName ?? parts.FirstOrDefault(part => !part.IsNumber && !part.IsState)?.Text;
        var state = parts.FirstOrDefault(part => part.IsState)?.Text;
        var canPick = Picker is not null && composer is { HasModelChoice: true, Disabled: false };
        var visible = model is { Enabled: true } && (model.HasAny || model.Usage is not null || name is not null);

        Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        modelButton.Visibility = canPick ? Visibility.Visible : Visibility.Collapsed;
        modelButton.IsEnabled = composer?.ChoosingModel is not true;
        current.Visibility = canPick ? Visibility.Collapsed : Visibility.Visible;
        current.Text = modelWords.Text = name ?? string.Empty;

        AutomationProperties.SetName(modelButton, $"{ComposerViewModel.ModelLabel}, {name}");
        ToolTipService.SetToolTip(modelButton, name);
        ToolTipService.SetToolTip(current, name);
        activity.Text = state ?? string.Empty;
        activity.Visibility = state is null ? Visibility.Collapsed : Visibility.Visible;
        activity.MaxWidth = Tokens.Number("DvMenuWidth") / 2;
        ToolTipService.SetToolTip(activity, state);
        PaintFigures(parts.Where(part => part.IsNumber), visible ? model?.Usage : null);
        Background = Tokens.Brush(this, "DvBg1");
        BorderBrush = Tokens.Brush(this, "DvLine");
        current.Foreground = Tokens.Brush(this, "DvText2");
        activity.Foreground = Tokens.Brush(this, "DvIon");
    }

    private void PaintFigures(IEnumerable<StatusPart> parts, ReplyUsageViewModel? usage)
    {
        popover.Model = usage;
        usageButton.Visibility = usage is null ? Visibility.Collapsed : Visibility.Visible;
        var words = parts.Select(part => part.Text).ToArray();
        AutomationProperties.SetName(usageButton, words.Length == 0
            ? ReplyUsageViewModel.Label
            : $"{ReplyUsageViewModel.Label}, {string.Join(", ", words)}");
        ToolTipService.SetToolTip(usageButton, ReplyUsageViewModel.Label);

        if (figures.Children.OfType<Border>().Select(part => (part.Child as TextBlock)?.Text)
            .SequenceEqual(words.Length == 0 ? ["Usage"] : words))
        {
            foreach (var rule in figures.Children.OfType<Border>())
            {
                rule.BorderBrush = Tokens.Brush(this, "DvLine");
                ((TextBlock)rule.Child).Foreground = Tokens.Brush(this, "DvText2");
            }

            return;
        }

        figures.Children.Clear();
        foreach (var word in words.Length == 0 ? ["Usage"] : words)
        {
            var label = Text();
            label.Text = word;
            label.Foreground = Tokens.Brush(this, "DvText2");
            figures.Children.Add(new Border
            {
                Child = label,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new(Tokens.Number("DvSpace1")),
                BorderBrush = Tokens.Brush(this, "DvLine"),
                BorderThickness = new(Tokens.Number("DvBorder"), 0, 0, 0),
            });
        }
    }

    #endregion
}
