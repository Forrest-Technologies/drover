namespace Drover.Native.Chat;

/// <summary>
/// The search box above the crew, and what it found in its place: each message a link that opens its conversation
/// scrolled to it. Enter searches, Escape clears, and the arrow keys move among the results. While nothing is
/// searched for, the crew shows as it always does.
/// </summary>
public sealed class ConversationSearch : StackPanel
{
    #region Private Fields

    private readonly ConversationSearchViewModel model;

    private readonly Field field = new()
    {
        Label = ConversationSearchViewModel.Label,
        Placeholder = ConversationSearchViewModel.Placeholder,
    };

    private readonly IconButton clear = new()
    {
        Icon = IconName.Close,
        Label = ConversationSearchViewModel.ClearAction,
        VerticalAlignment = VerticalAlignment.Bottom,
    };

    private readonly TextBlock status = Styles.Text(null, "DvTextCaption");

    private readonly EmptyState failed = new()
    {
        Title = ConversationSearchViewModel.FailedTitle,
        Cause = ConversationSearchViewModel.FailedTitle,
        ActionLabel = ConversationSearchViewModel.TryAgain,
    };

    private readonly EmptyState nothing = new()
    {
        Cause = ConversationSearchViewModel.NothingCause,
        ActionLabel = ConversationSearchViewModel.ClearAction,
    };

    private readonly StackPanel list = new() { Spacing = Tokens.Number("DvSpace1") };

    private readonly ArrowKeyZone results = new() { Label = "Messages found" };

    #endregion

    #region Constructors

    public ConversationSearch()
        : this(Pieces.Make<ConversationSearchViewModel>())
    {
    }

    /// <summary>A search with a view model made elsewhere, for the gallery, which reads from a sample server.</summary>
    internal ConversationSearch(ConversationSearchViewModel model)
    {
        this.model = model;
        Spacing = Tokens.Number("DvSpace2");
        Padding = new(0, 0, 0, Tokens.Number("DvSpace2"));

        var box = new Grid { ColumnSpacing = Tokens.Number("DvSpace1") };
        box.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        box.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumn(clear, 1);
        box.Children.Add(field);
        box.Children.Add(clear);

        results.Items = list;
        Pieces.SayWhenShown(status);
        Children.Add(box);
        Children.Add(status);
        Children.Add(failed);
        Children.Add(nothing);
        Children.Add(results);

        field.RegisterPropertyChangedCallback(Field.ValueProperty, (_, _) => model.Query = field.Value);
        field.Submitted += async (_, _) => await model.Search();
        field.KeyDown += (_, key) =>
        {
            if (key.Key is VirtualKey.Escape && (model.IsActive || field.Value.Length > 0))
            {
                key.Handled = true;
                ClearAndStay();
            }
        };
        clear.Click += (_, _) => ClearAndStay();
        failed.Action += async (_, _) => await model.Search();
        nothing.Action += (_, _) => ClearAndStay();

        model.PropertyChanged += (_, _) => Draw();
        Draw();
    }

    #endregion

    #region Properties

    /// <summary>True while results, or the lack of them, are shown in place of the crew.</summary>
    public bool IsActive => model.IsActive;

    /// <summary>Raised when the search starts or stops standing in place of the crew.</summary>
    public event Action? ActiveChanged;

    #endregion

    #region Public Methods

    /// <summary>Types the words into the box and searches for them, as a person would, for the gallery.</summary>
    internal Task SearchFor(string words)
    {
        field.Value = words;
        model.Query = words;
        return model.Search();
    }

    #endregion

    #region Private Methods

    // Clearing puts the person back in the box, so they can type the next words at once.
    private void ClearAndStay()
    {
        model.Clear();
        field.Value = string.Empty;
        field.TakeFocus();
    }

    private void Draw()
    {
        var state = model.State;
        var screen = ConversationSearchScreen.For(state, model.IsActive);
        clear.Visibility = Shown(screen.ShowsClear);
        failed.Visibility = Shown(screen.ShowsFailed);
        failed.Cause = model.Problem ?? ConversationSearchViewModel.FailedTitle;
        nothing.Visibility = Shown(screen.ShowsNothing);
        nothing.Title = model.NothingTitle;
        results.Visibility = Shown(screen.ShowsResults);
        Say(model.Status);
        ShowHits(screen.ShowsResults ? model.Hits : []);
        ActiveChanged?.Invoke();
    }

    private void Say(string? words)
    {
        status.Visibility = Shown(words is not null);

        if (status.Text != (words ?? string.Empty))
        {
            status.Text = words ?? string.Empty;
            FrameworkElementAutomationPeer.FromElement(status)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    // A row is kept for as long as its message is listed, so the focus stays on it while the list changes around it.
    private void ShowHits(IReadOnlyList<SearchHitRow> hits)
    {
        var kept = list.Children.OfType<FacetLink>().ToDictionary(link => (Guid)link.Tag);
        list.Children.Clear();

        foreach (var hit in hits)
        {
            var link = kept.GetValueOrDefault(hit.Key) ?? new FacetLink { Tag = hit.Key };
            Describe(link, hit);
            list.Children.Add(link);
        }
    }

    private static void Describe(FacetLink link, SearchHitRow hit)
    {
        var avatar = new Avatar { BotName = hit.BotName, Hue = hit.BotHue, Size = 24, VerticalAlignment = VerticalAlignment.Center };
        var heading = new TextBlock { Text = hit.Heading, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var top = new Grid { ColumnSpacing = Tokens.Number("DvSpace2") };
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Grid.SetColumn(heading, 1);
        top.Children.Add(avatar);
        top.Children.Add(heading);

        var column = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
        column.Children.Add(top);
        column.Children.Add(Snippet(hit));
        column.Children.Add(Styles.Text(hit.Facts, "DvTextCaption"));

        link.Content = column;
        link.Address = hit.Href;
        link.Padding = Tokens.Space("DvSpace3", "DvSpace2");
        link.MinHeight = Tokens.Number("DvTargetTouch");
        AutomationProperties.SetName(link, hit.AccessibleName);
        ArrowKeyZone.SetIsItem(link, true);
        link.Refresh();
    }

    // The matched words are set in the heading weight, so a match is never shown by color alone.
    private static TextBlock Snippet(SearchHitRow hit)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightBody"],
        };
        var at = 0;

        foreach (var match in hit.Matches.Where(match => match.Start >= 0 && match.Start + match.Length <= hit.Snippet.Length).OrderBy(match => match.Start))
        {
            if (match.Start < at)
            {
                continue;
            }

            text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = hit.Snippet[at..match.Start] });
            text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = hit.Snippet.Substring(match.Start, match.Length),
                FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightHeading"],
            });
            at = match.Start + match.Length;
        }

        text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = hit.Snippet[at..] });
        return text;
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    #endregion
}
