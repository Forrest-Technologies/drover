namespace Drover.Native.Chat;

/// <summary>
/// The console a person writes to a bot in: the field and its actions in one frame. Enter sends and Shift+Enter
/// breaks the line; on a touch keyboard Enter breaks the line and Send sends. The field grows with its text up to
/// a set number of lines. Files chosen with Attach, pasted into the field, or dropped on the conversation all go the
/// same way. The plus button opens message actions above the field. While the bot works, a line travels around the
/// frame and Stop stands where Send was.
/// </summary>
public sealed class Composer : Grid
{
    #region Private Fields

    private readonly StackPanel column = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly AttachmentTray tray = new();

    private readonly MenuItem attach = new();

    private readonly MenuItem pickModel = new() { Content = ComposerViewModel.ModelLabel, OpensFlyout = true };

    private readonly Facet frame = new();

    private readonly Grid row = new() { ColumnSpacing = Tokens.Number("DvSpace2") };

    private readonly TravelLine travel = new();

    private readonly Menu more = new()
    {
        TriggerIcon = IconName.Plus,
        Label = "More options",
        ShowTriggerLabel = false,
        Placement = FlyoutPlacementMode.TopEdgeAlignedLeft,
        VerticalAlignment = VerticalAlignment.Bottom,
    };

    private readonly TextBox field = new()
    {
        Template = Styles.Template("DvTextBoxTemplate"),
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        UseSystemFocusVisuals = false,
        VerticalAlignment = VerticalAlignment.Bottom,
    };

    private readonly TextBlock placeholder = Styles.Text(null);

    private readonly Border focusLine = new() { VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };

    private readonly Grid fieldBox = new();

    private readonly Button stop = new() { VerticalAlignment = VerticalAlignment.Bottom };

    private readonly Button send = new() { Kind = ButtonKind.Primary, VerticalAlignment = VerticalAlignment.Bottom };

    // Saying a message: the microphone, which is Stop while it records, the way to throw the recording away, and
    // beside them the time recorded or that the words are being written down.
    private readonly Button microphone = new() { VerticalAlignment = VerticalAlignment.Bottom };

    private readonly Button discard = new() { VerticalAlignment = VerticalAlignment.Bottom };

    private readonly TextBlock voiceStatus = Styles.Text(null, "DvTextLabel");

    private readonly SpokenWords spoken = new();

    private readonly VoiceComposerViewModel voice;

    private readonly Icon microphoneMark;

    private readonly TextBlock dropNote = Styles.Text("Drop the files to attach them.", "DvTextLabel");

    private ComposerViewModel? model;

    private bool fieldFocused;

    private bool focusWithin;

    private bool? stacked;

    private bool focusWanted;

    private bool modelPickerInMenu;

    #endregion

    #region Constructors

    public Composer()
    {
        MaxWidth = Tokens.Number("DvMeasure") + Tokens.Number("DvSpace6");

        voice = Pieces.Make<VoiceComposerViewModel>(this);
        Labelled(attach, IconName.Attach, "Attach file");
        more.Trigger.Padding = new(0);
        more.Trigger.MinWidth = Targets.Minimum;
        ToolTipService.SetToolTip(more.Trigger, more.Label);
        more.Items.Add(attach);
        IconOnly(stop, IconName.Stop, StopLabel);
        IconOnly(send, IconName.Send, SendLabel);
        if (OperatingSystem.IsAndroid())
        {
            // Moving focus on touch-down hides the keyboard and moves Send before the finger lifts.
            send.AllowFocusOnInteraction = false;
        }

        microphoneMark = IconOnly(microphone, IconName.Microphone, VoiceWords.Record);
        IconOnly(discard, IconName.Close, VoiceWords.DiscardRecording);
        AutomationProperties.SetAutomationId(microphone, "ComposerMicrophone");
        AutomationProperties.SetAutomationId(discard, "ComposerDiscardRecording");
        AutomationProperties.SetName(this, "Message composer");
        AutomationProperties.SetName(frame, "Write a message");
        AutomationProperties.SetAutomationId(field, "ComposerField");
        AutomationProperties.SetAutomationId(send, "ComposerSend");
        AutomationProperties.SetAutomationId(stop, "ComposerStop");
        AutomationProperties.SetAutomationId(attach, "ComposerAttach");
        AutomationProperties.SetAutomationId(more.Trigger, "ComposerMore");

        BuildField();
        BuildRow();

        dropNote.Visibility = Visibility.Collapsed;
        column.Children.Add(tray);
        column.Children.Add(frame);
        column.Children.Add(dropNote);
        column.Children.Add(spoken);
        Children.Add(column);

        attach.Selected += async (_, _) => await Pick();
        pickModel.Selected += (_, _) =>
        {
            if (model is { } shown)
            {
                Picker?.Open(more.Trigger, shown);
            }
        };
        stop.Click += async (_, _) => await (model?.Cancel() ?? Task.CompletedTask);
        send.Click += async (_, _) => await Send();
        tray.Remove += async (_, id) => await (model?.Remove(id) ?? Task.CompletedTask);
        microphone.Click += async (_, _) => await voice.Press();
        discard.Click += async (_, _) => await voice.Discard();
        voice.PropertyChanged += Changed;

        Loaded += async (_, _) =>
        {
            PlatformPaste.Join(this);
            await voice.Open();
        };
        Unloaded += async (_, _) =>
        {
            PlatformPaste.Leave(this);
            await voice.Leave();
        };

        frame.GotFocus += (_, _) => SetWithin(true);
        frame.LostFocus += (_, _) => SetWithin(false);
        SizeChanged += (_, _) => Fit();
        this.WhenShownOrRethemed(Paint);
        Show();
    }

    #endregion

    #region Properties

    /// <summary>What the composer shows and whom it tells: set by the conversation it belongs to.</summary>
    public ComposerViewModel? Model
    {
        get => model;
        set
        {
            if (model is not null)
            {
                model.PropertyChanged -= Changed;
                model.FocusRequested -= TakeFocus;
            }

            model = value;
            voice.OnWritten = model is null ? null : model.Dictate;

            // A recording that came to nothing is said the way a file that could not be attached is: once, in a toast.
            voice.OnFailed = model is null ? null : words => model.Notify(words, ToastKind.Error);

            if (model is not null)
            {
                model.PropertyChanged += Changed;
                model.FocusRequested += TakeFocus;
            }

            Show();
        }
    }

    internal ConversationModelPicker? Picker { get; init; }

    internal bool ModelPickerInMenu
    {
        get => modelPickerInMenu;
        set
        {
            if (modelPickerInMenu != value)
            {
                modelPickerInMenu = value;
                ShowMenu();
            }
        }
    }

    /// <summary>
    /// The conversation a paste in a browser goes to, asked while the paste happens: this one when its field has the
    /// cursor and it takes files, or none.
    /// </summary>
    internal ComposerViewModel? PasteDestination => fieldFocused && model is { TakesFiles: true } ? model : null;

    #endregion

    #region Public Methods

    /// <summary>Puts the cursor in the message field. It waits its turn, so it comes after whatever moved the focus just now.</summary>
    public void TakeFocus()
    {
        focusWanted = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, FocusField);
    }

    /// <summary>
    /// The frame takes the ember edge and the line of words says it will take the files held over the conversation.
    /// </summary>
    public void ShowDropping(bool value)
    {
        if (model is { } shown)
        {
            shown.ShowDropping(value);
            return;
        }

        dropNote.Visibility = Visibility.Collapsed;
        Paint();
    }

    /// <summary>Takes the files dropped on the conversation. What cannot be taken is said in one sentence.</summary>
    public async Task TakeDropped(Windows.ApplicationModel.DataTransfer.DataPackageView dropped)
    {
        ShowDropping(false);

        if (model is { } shown)
        {
            await shown.TakeDropped(() => PickedFiles.Dropped(dropped), Log.For<Composer>());
        }
    }

    #endregion

    #region Building

    private void BuildField()
    {
        var size = Tokens.Number(Targets.IsCoarse ? "DvText16" : "DvText14");
        var line = size * Tokens.Number("DvLeadingText");
        var block = Tokens.Number(Targets.IsCoarse ? "DvSpace3" : "DvSpace2");
        var inside = new Thickness(Tokens.Number("DvSpace3"), block, Tokens.Number("DvSpace3"), block);

        field.Padding = inside;
        field.FontFamily = Tokens.FontUi;
        field.FontSize = placeholder.FontSize = size;
        field.MinHeight = Targets.Minimum;
        field.MaxHeight = (Tokens.Number("DvComposerLines") * line) + (2 * block);
        ScrollViewer.SetVerticalScrollBarVisibility(field, ScrollBarVisibility.Auto);
        field.TextChanged += (_, _) => Typed();
        field.PreviewKeyDown += Pressed;
        field.Paste += async (_, pasted) => await Pasted(pasted);
        field.Loaded += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, FocusField);
        field.GotFocus += (_, _) => SetFieldFocused(true);
        field.LostFocus += (_, _) => SetFieldFocused(false);

        placeholder.Margin = inside;
        placeholder.IsHitTestVisible = false;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.TextWrapping = TextWrapping.NoWrap;
        placeholder.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetAccessibilityView(placeholder, AccessibilityView.Raw);

        // The frame shows the focus for the field inside it: a line along the field's foot.
        focusLine.Height = Tokens.Number("DvFocusWidth");
        fieldBox.Children.Add(field);
        fieldBox.Children.Add(placeholder);
        fieldBox.Children.Add(focusLine);
    }

    private void BuildRow()
    {
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });

        // After the field: discard, the time recorded, the microphone, Stop, Send.
        FrameworkElement[] actions = [discard, voiceStatus, microphone, stop, send];
        foreach (var _ in actions)
        {
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        }

        row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        row.Children.Add(more);
        row.Children.Add(fieldBox);
        foreach (var action in actions)
        {
            row.Children.Add(action);
        }

        voiceStatus.VerticalAlignment = VerticalAlignment.Bottom;
        voiceStatus.TextWrapping = TextWrapping.NoWrap;
        discard.Visibility = microphone.Visibility = voiceStatus.Visibility = Visibility.Collapsed;

        var pad = Tokens.Number("DvSpace2");
        frame.Padding = new(pad);
        travel.Margin = TravelLine.Over(frame.Padding);
        frame.Children.Add(row);
        frame.Children.Add(travel);
    }

    // An icon and the word for what the button does. The icon takes the color the button's words have.
    // Send and Stop are known by their icons. Each keeps its name in words, as its accessible name and its tooltip, and
    // a full touch target whatever the pointer, since it is the composer's main action.
    private static Icon IconOnly(Button button, IconName icon, string label)
    {
        var mark = new Icon { NameOfIcon = icon, Size = 20, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        button.Content = mark;
        button.Padding = new(0);
        button.Width = button.Height = Tokens.Number("DvTargetTouch");
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        button.RegisterPropertyChangedCallback(Control.ForegroundProperty, (_, _) => mark.Foreground = button.Foreground);
        return mark;
    }

    private static void Labelled(MenuItem button, IconName icon, string label)
    {
        var mark = new Icon { NameOfIcon = icon, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        content.Children.Add(mark);
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        button.Content = content;
        AutomationProperties.SetName(button, label);
        button.RegisterPropertyChangedCallback(Control.ForegroundProperty, (_, _) => mark.Foreground = button.Foreground);
    }

    #endregion

    #region Showing

    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs change) => Show();

    private void Show()
    {
        var shown = model;

        ShowVoice(shown);
        ShowMenu();

        if (shown is null)
        {
            dropNote.Visibility = Visibility.Collapsed;
            Paint();
            return;
        }

        if (field.Text != shown.Text)
        {
            field.Text = shown.Text;
            field.SelectionStart = field.Text.Length;
        }

        field.IsEnabled = !shown.Disabled;
        placeholder.Text = shown.Placeholder;
        placeholder.Visibility = field.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(field, shown.FieldName);
        AutomationProperties.SetHelpText(field, shown.FieldDescription);

        tray.BotName = shown.BotName;
        tray.Items = shown.Attachments;
        stop.Visibility = shown.ShowsStop ? Visibility.Visible : Visibility.Collapsed;
        send.Visibility = shown.ShowsSend && voice.Phase is VoicePhase.Idle ? Visibility.Visible : Visibility.Collapsed;
        send.IsEnabled = shown.CanSend;
        travel.IsRunning = shown.IsWorking;
        dropNote.Visibility = shown.Dropping ? Visibility.Visible : Visibility.Collapsed;
        Fit();
        Paint();
    }

    // The microphone exists only where something can listen. While it records it is Stop, with the time beside it
    // and the way to throw the recording away; while the words are written down, it waits and says so.
    private void ShowVoice(ComposerViewModel? shown)
    {
        var listens = shown is not null && voice.Shows;
        var busy = listens && voice.Phase is not VoicePhase.Idle;
        var recording = voice.Phase is VoicePhase.Recording;
        microphone.Visibility = listens ? Visibility.Visible : Visibility.Collapsed;
        microphone.IsEnabled = shown is { Disabled: false } && voice.Phase is not VoicePhase.Writing;
        microphoneMark.NameOfIcon = recording ? IconName.Stop : IconName.Microphone;
        AutomationProperties.SetName(microphone, voice.ButtonLabel);
        ToolTipService.SetToolTip(microphone, voice.ButtonLabel);
        discard.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        voiceStatus.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        voiceStatus.Text = recording ? voice.Elapsed : VoiceWords.WritingItDown;
        voiceStatus.FontFamily = recording ? Tokens.FontMono : Tokens.FontUi;
        voiceStatus.Margin = new(0, 0, 0, (Tokens.Number("DvTargetTouch") - voiceStatus.LineHeight) / 2);
        spoken.Say(listens ? voice.Announcement : null);
    }

    private void ShowMenu()
    {
        attach.IsEnabled = model?.TakesFiles is true;
        var offersModel = modelPickerInMenu && Picker is not null && model is { HasModelChoice: true };
        pickModel.IsEnabled = model is { Disabled: false, ChoosingModel: false };

        if (offersModel && !more.Items.Contains(pickModel))
        {
            more.Items.Add(pickModel);
        }
        else if (!offersModel)
        {
            more.Items.Remove(pickModel);
        }

        more.Trigger.IsEnabled = attach.IsEnabled || (offersModel && pickModel.IsEnabled);
    }

    // A narrow window with Stop and Send side by side gives the field the whole first row and puts the two under it.
    private void Fit()
    {
        var phone = ActualWidth > 0 && ActualWidth < (double)Styles.Dictionary["DvBreakPhone"];
        var two = phone && (model?.HasTwoActions ?? false);
        var inline = Tokens.Number(phone ? "DvSpace2" : "DvSpace4");
        column.Padding = new(inline, Tokens.Number("DvSpace3"), inline, Tokens.Number("DvSpace3"));

        if (two == stacked)
        {
            return;
        }

        stacked = two;
        row.RowSpacing = two ? Tokens.Number("DvSpace2") : 0;
        SetColumn(fieldBox, 1);
        SetColumnSpan(fieldBox, two ? row.ColumnDefinitions.Count - 1 : 1);
        FrameworkElement[] actions = [discard, voiceStatus, microphone, stop, send];
        for (var index = 0; index < actions.Length; index++)
        {
            SetColumn(actions[index], index + 2);
            SetRow(actions[index], two ? 1 : 0);
        }
    }

    private void Paint()
    {
        frame.Fill = Tokens.Brush(this, "DvBg1");
        frame.Edge = Tokens.Brush(this, focusWithin || (model?.Dropping ?? false) ? "DvEmber" : "DvText3");
        field.Foreground = Tokens.Brush(this, model?.Disabled is true ? "DvText3" : "DvText");
        placeholder.Foreground = Tokens.Brush(this, "DvText3");
        focusLine.Background = fieldFocused ? Tokens.Brush(this, "DvEmber") : null;
        dropNote.Foreground = voiceStatus.Foreground = Tokens.Brush(this, "DvText2");
    }

    private void SetFieldFocused(bool value)
    {
        fieldFocused = value;
        Paint();
    }

    private void SetWithin(bool value)
    {
        focusWithin = value;
        Paint();
    }

    // The field may be between two pages when the cursor is asked for: then it takes the cursor once it is shown.
    private void FocusField()
    {
        if (focusWanted && field.IsLoaded && field.Focus(FocusState.Programmatic))
        {
            focusWanted = false;
        }
    }

    #endregion

    #region Writing And Sending

    private void Typed()
    {
        placeholder.Visibility = field.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (model is not null && model.Text != field.Text)
        {
            model.Text = field.Text;
        }
    }

    // Enter sends and Shift+Enter breaks the line, as on a desktop. On a touch keyboard Enter breaks the line and the
    // Send button sends.
    private async void Pressed(object sender, KeyRoutedEventArgs key)
    {
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (key.Key is not VirtualKey.Enter || shift || Targets.IsCoarse)
        {
            return;
        }

        // The change event for the last letters can still be on its way when Enter arrives; send what the field holds.
        key.Handled = true;
        Typed();
        await Send();
    }

    private async Task Send()
    {
        if (model is not null)
        {
            await model.Send();
        }
    }

    #endregion

    #region Taking Files

    private async Task Pick()
    {
        if (model is not { } shown)
        {
            return;
        }

        HandedFiles chosen;
        try
        {
            chosen = await PickedFiles.Choose();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A device with no file chooser, or one that fails: the composer stays as it was, and the log says why.
            Log.For<Composer>().LogError(exception, "The device's file chooser could not be opened");
            shown.Notify(FileWords.ChooserFailed, ToastKind.Error);
            return;
        }

        await shown.Hand(chosen);
    }

    // The field is told whether to paste before the first wait, so what the clipboard holds is looked at at once. In
    // a browser the page's paste event brings files and pictures instead (PlatformPaste), and this leaves it alone.
    private async Task Pasted(TextControlPasteEventArgs pasted)
    {
        if (OperatingSystem.IsBrowser() || model is not { } shown)
        {
            return;
        }

        Windows.ApplicationModel.DataTransfer.DataPackageView? carried = null;
        var logger = Log.For<Composer>();

        if (shown.Pasting(Look, logger) && carried is { } held)
        {
            pasted.Handled = true;
            await shown.TakePasted(() => PickedFiles.Pasted(held), logger);
        }

        ClipboardHolds Look()
        {
            carried = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            return PickedFiles.Holds(carried);
        }
    }

    #endregion

    #region Fixed Words

    public const string SendLabel = "Send";

    public const string StopLabel = "Stop";

    #endregion
}
