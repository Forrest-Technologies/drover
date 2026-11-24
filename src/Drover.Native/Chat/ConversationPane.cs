namespace Drover.Native.Chat;

/// <summary>
/// One conversation with a bot: what was said, what the bot is doing, and the field to write in. It fills the
/// column it is put in: the messages scroll, the composer stays at the foot. A bot's side of work handed to it has
/// a line of words where the field would be. Files dropped anywhere on it are attached to it.
/// </summary>
public sealed class ConversationPane : Grid
{
    #region Private Fields

    private readonly MessageList list = new();

    private readonly Composer composer = new();

    private readonly TextBlock loading = Styles.Text(ConversationViewModel.LoadingHint);

    // One state for a conversation that could not be read, kept while it is read again so the button keeps the focus.
    private readonly EmptyState loadFailed = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    private readonly Border center = new() { Padding = new(Tokens.Number("DvSpace4")) };

    private readonly Border readBox = new();

    private readonly TextBlock readHint = Styles.Text(null);

    private ConversationViewModel? model;

    private EmptyNotice? shownMissing;

    private EmptyNotice? shownLoadFailed;

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
        AllowDrop = true;
        DragOver += Carried;
        DragLeave += (_, _) => composer.ShowDropping(false);
        Drop += async (_, dropped) => await Dropped(dropped);

        // The button stays pressable while the conversation is read again: Reload does nothing until the read ends,
        // so a second press neither asks again nor takes the focus off the way forward.
        loadFailed.Action += (_, _) => model?.Reload();

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
        var failed = model?.LoadFailed ?? false;
        var retrying = waiting && shownLoadFailed is not null;

        var listShown = !missing && !failed && !waiting;

        // The list draws what it has before it is shown, so the first frame of a loaded conversation is its messages,
        // not what the list drew while the conversation was still being read.
        if (listShown && list.Visibility is Visibility.Collapsed)
        {
            list.DrawNow();
        }

        list.Visibility = Shown(listShown);
        center.Visibility = Shown(missing || waiting || failed);
        composer.Visibility = Shown(!missing && !reading && model is not null);
        readBox.Visibility = Shown(!missing && reading);
        readHint.Text = model?.ReadHint ?? string.Empty;

        if (missing)
        {
            shownLoadFailed = null;
            if (model!.MissingNotice != shownMissing)
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
        }
        else if (failed || retrying)
        {
            if (failed && model!.LoadFailedNotice != shownLoadFailed)
            {
                shownLoadFailed = model.LoadFailedNotice;
                loadFailed.ActionLabel = shownLoadFailed.ActionLabel;
                loadFailed.ActionHref = shownLoadFailed.ActionHref;
            }

            // While the read is on its way the loading line takes the place of the failure words. The state and its
            // button stay, so a second failure with a new sentence does not move the person's place.
            loadFailed.Title = retrying ? null : shownLoadFailed!.Title;
            loadFailed.Cause = retrying ? ConversationViewModel.LoadingHint : shownLoadFailed!.Cause;
            if (!ReferenceEquals(center.Child, loadFailed))
            {
                center.Child = loadFailed;
            }
        }
        else
        {
            shownMissing = null;
            shownLoadFailed = null;
            if (!ReferenceEquals(center.Child, loading))
            {
                center.Child = loading;
            }
        }

        Paint();
    }

    // Files held over the conversation, its messages as much as its field: the composer's frame and a line of words say
    // it will take them. Anything else, or a conversation that cannot take files now, is refused, the cursor says so,
    // and the line goes if it was shown.
    private void Carried(object sender, DragEventArgs held)
    {
        var takes = Takes(held.DataView);
        held.AcceptedOperation = takes
            ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy
            : Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
        composer.ShowDropping(takes);
    }

    // The same check as while the files were held: a drop can come without a hover that was accepted, or the
    // conversation can stop taking files between hover and release. Such a drop attaches nothing.
    private async Task Dropped(DragEventArgs dropped)
    {
        composer.ShowDropping(false);

        if (!Takes(dropped.DataView))
        {
            dropped.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            return;
        }

        await composer.TakeDropped(dropped.DataView);
    }

    // A drop is taken only when it carries files and the conversation takes files now. A picture dragged with no file
    // behind it is not one.
    private bool Takes(Windows.ApplicationModel.DataTransfer.DataPackageView carried) =>
        model is { AcceptsFiles: true }
        && carried.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems);

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private void Paint()
    {
        loading.Foreground = readHint.Foreground = Tokens.Brush(this, "DvText2");
        readBox.BorderBrush = Tokens.Brush(this, "DvLine");
    }

    #endregion
}
