namespace Drover.Native.Chat;

/// <summary>
/// The files a person and a bot have exchanged, beside the conversation, newest first. Its job is finding one again.
/// The one filter appears only when files came from both sides.
/// </summary>
public sealed class FilesPanel : Grid
{
    #region Private Fields

    private readonly FilesPanelViewModel model = new();

    private readonly Batch batch;

    private readonly StackPanel column = new() { Spacing = Tokens.Number("DvSpace4"), Padding = new(Tokens.Number("DvSpace4")) };

    private readonly TextBlock hint = Styles.Text(null, "DvTextSecondary");

    private readonly EmptyState failed = new() { Title = FilesPanelViewModel.FailedTitle, ActionLabel = FilesPanelViewModel.TryAgain };

    private readonly SegmentedControl filter = new() { Label = FilesPanelViewModel.FilterName };

    private readonly StackPanel list = new() { Spacing = Tokens.Number("DvSpace4") };

    // Kept by file, so a card that has shown all of its text still has it after the list is drawn again.
    private readonly Dictionary<Guid, FileCard> cards = [];

    #endregion

    #region Constructors

    public FilesPanel()
    {
        batch = new(this, Apply, Draw);
        Pieces.SayWhenShown(hint);
        failed.Cause = model.FailedCause;
        failed.Action += (_, _) => Retry?.Invoke(this, EventArgs.Empty);
        filter.RegisterPropertyChangedCallback(SegmentedControl.ValueProperty, (_, _) => Choose());

        column.Children.Add(hint);
        column.Children.Add(failed);
        column.Children.Add(filter);
        column.Children.Add(list);
        Children.Add(new ScrollViewer { Content = column, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled, IsTabStop = false });

        model.PropertyChanged += (_, _) => batch.Draw();
        this.WhenShownOrRethemed(batch.Draw);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty StateProperty =
        Dp.Register<FilesPanel, FilesPanelState>(nameof(State), FilesPanelState.Loading, panel => panel.batch.Values());

    public static readonly DependencyProperty BotNameProperty = Dp.Register<FilesPanel, string>(nameof(BotName), string.Empty, panel => panel.batch.Values());

    public static readonly DependencyProperty FilesProperty =
        Dp.Register<FilesPanel, IReadOnlyList<SharedFile>>(nameof(Files), [], panel => panel.batch.Values());

    public static readonly DependencyProperty ProblemProperty = Dp.Register<FilesPanel, string?>(nameof(Problem), null, panel => panel.batch.Values());

    public static readonly DependencyProperty ReadWholeProperty =
        Dp.Register<FilesPanel, Func<SharedFile, Task<FileText>>?>(nameof(ReadWhole), null, panel => panel.batch.Values());

    /// <summary>What the panel has to show. Required.</summary>
    public FilesPanelState State
    {
        get => (FilesPanelState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>The bot the files were exchanged with. Required.</summary>
    public string BotName
    {
        get => (string)GetValue(BotNameProperty);
        set => SetValue(BotNameProperty, value);
    }

    /// <summary>The files, newest first.</summary>
    public IReadOnlyList<SharedFile> Files
    {
        get => (IReadOnlyList<SharedFile>)GetValue(FilesProperty);
        set => SetValue(FilesProperty, value);
    }

    public string? Problem
    {
        get => (string?)GetValue(ProblemProperty);
        set => SetValue(ProblemProperty, value);
    }

    /// <summary>Reads a text file's whole content for "Show all".</summary>
    public Func<SharedFile, Task<FileText>>? ReadWhole
    {
        get => (Func<SharedFile, Task<FileText>>?)GetValue(ReadWholeProperty);
        set => SetValue(ReadWholeProperty, value);
    }

    /// <summary>Raised when the person asks to load the files again after that failed.</summary>
    public event EventHandler? Retry;

    #endregion

    #region Private Methods

    private void Apply() =>
        (model.State, model.BotName, model.Files, model.Problem, model.ReadWhole) = (State, BotName, Files, Problem, ReadWhole);

    private void Choose()
    {
        if (Enum.TryParse<FilesFrom>(filter.Value, out var from))
        {
            model.From = from;
        }
    }

    private void Draw()
    {
        var listed = model.State is FilesPanelState.Ready && model.Files.Count > 0;
        var said = model.State switch
        {
            FilesPanelState.Loading => FilesPanelViewModel.Loading,
            FilesPanelState.Ready when model.Files.Count is 0 => model.EmptyHint,
            _ => null,
        };

        hint.Text = said ?? string.Empty;
        hint.Visibility = said is null ? Visibility.Collapsed : Visibility.Visible;
        failed.Visibility = model.State is FilesPanelState.Failed ? Visibility.Visible : Visibility.Collapsed;
        failed.Cause = model.FailedCause;
        filter.Visibility = listed && model.ShowsFilter ? Visibility.Visible : Visibility.Collapsed;
        list.Visibility = listed ? Visibility.Visible : Visibility.Collapsed;

        if (!listed)
        {
            return;
        }

        filter.Options = [.. model.Sides.Select(side => new SegmentOption<string>(side.Value.ToString(), side.Label))];
        filter.Value = model.From.ToString();
        AutomationProperties.SetName(list, model.ListName);
        DrawList();
    }

    private void DrawList()
    {
        List<SharedFile> shown = [.. model.Shown];
        foreach (var gone in cards.Keys.Where(id => model.Files.All(file => file.Id != id)).ToList())
        {
            cards.Remove(gone);
        }

        list.Children.Clear();
        foreach (var file in shown)
        {
            if (!cards.TryGetValue(file.Id, out var card))
            {
                cards[file.Id] = card = new FileCard();
            }

            (card.File, card.ReadWhole) = (file, model.ReadWhole);
            var item = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
            item.Children.Add(Pieces.Detached(card));
            item.Children.Add(Origin(file));
            list.Children.Add(item);
        }
    }

    // Who the file came from and when, and the way back to that moment of the conversation.
    private TextBlock Origin(SharedFile file)
    {
        var line = Styles.Text(model.Origin(file), "DvTextMono");
        if (file.MomentHref is { } moment)
        {
            var link = Pieces.LinkIn(this, FilesPanelViewModel.OpenMoment, moment, "DvText2");
            AutomationProperties.SetName(link, FilesPanelViewModel.OpenMomentName(file));
            line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "  " });
            line.Inlines.Add(link);
        }

        return line;
    }

    #endregion
}
