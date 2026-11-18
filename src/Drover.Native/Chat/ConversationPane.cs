namespace Drover.Native.Chat;

/// <summary>
/// One conversation with a bot: what was said, what the bot is doing, and the field to write in. It fills the
/// column it is put in: the messages scroll, the composer stays at the foot. A bot's side of work handed to it has
/// a line of words where the field would be.
/// </summary>
public sealed class ConversationPane : Grid
{
    #region Private Fields

    private readonly MessageList list = new();

    private readonly Composer composer = new();

    private readonly TextBlock loading = Styles.Text(ConversationViewModel.LoadingHint);

    private readonly Border center = new() { Padding = new(Tokens.Number("DvSpace4")) };

    private readonly Border readBox = new();

    private readonly TextBlock readHint = Styles.Text(null);

    private ConversationViewModel? model;

    private EmptyNotice? shownMissing;

    private bool drawQueued;

    #endregion

    #region Constructors

    public ConversationPane()
    {
        RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });

        loading.HorizontalAlignment = HorizontalAlignment.Center;
        loading.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetLiveSetting(loading, AutomationLiveSetting.Polite);

        readBox.Child = readHint;
        readBox.Padding = Tokens.Space("DvSpace4", "DvSpace3");
        readBox.BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0);
        SetRow(composer, 1);
        SetRow(readBox, 1);

        Children.Add(list);
        Children.Add(center);
        Children.Add(composer);
        Children.Add(readBox);
        this.WhenShownOrRethemed(Paint);
        Show();
    }

    #endregion

    #region Properties

    /// <summary>The conversation this shows. Each conversation gets a pane of its own; a pane is not handed another.</summary>
    public ConversationViewModel? Model
    {
        get => model;
        set
        {
            model?.PropertyChanged -= Changed;
            model = value;
            model?.PropertyChanged += Changed;
            list.Model = value?.List;
            composer.Model = value?.Composer;
            Show();
        }
    }

    #endregion

    #region Private Methods

    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (!drawQueued)
        {
            drawQueued = true;
            DispatcherQueue.TryEnqueue(Show);
        }
    }

    private void Show()
    {
        drawQueued = false;
        var (missing, waiting, reading) = (model?.Missing ?? false, model?.Loading ?? true, model?.ReadOnly ?? false);

        list.Visibility = Shown(!missing && !waiting);
        center.Visibility = Shown(missing || waiting);
        composer.Visibility = Shown(!missing && !reading && model is not null);
        readBox.Visibility = Shown(!missing && reading);
        readHint.Text = model?.ReadHint ?? string.Empty;

        if (!missing)
        {
            shownMissing = null;
            if (!ReferenceEquals(center.Child, loading))
            {
                center.Child = loading;
            }
        }
        else if (model!.MissingNotice != shownMissing)
        {
            var notice = shownMissing = model.MissingNotice;
            center.Child = new EmptyState
            {
                Title = notice.Title,
                Cause = notice.Cause,
                ActionLabel = notice.ActionLabel,
                ActionHref = notice.ActionHref,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        Paint();
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private void Paint()
    {
        loading.Foreground = readHint.Foreground = Tokens.Brush(this, "DvText2");
        readBox.BorderBrush = Tokens.Brush(this, "DvLine");
    }

    #endregion
}
