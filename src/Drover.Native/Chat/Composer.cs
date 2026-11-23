namespace Drover.Native.Chat;

/// <summary>
/// The console a person writes to a bot in: the field and its actions in one frame. Enter sends and Shift+Enter
/// breaks the line; on a touch keyboard Enter breaks the line and Send sends. The field grows with its text up to
/// a set number of lines. Files chosen with Attach, pasted into the field, or dropped on the conversation all go the
/// same way. While the bot works, a line travels the frame's top edge and Stop stands where Send was. The model the
/// conversation answers with is chosen behind the plus button, beside Attach, when the bot may use more than one.
/// </summary>
public sealed class Composer : Grid
{
    #region Private Fields

    private readonly StackPanel column = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly AttachmentTray tray = new();

    private readonly FlowPanel options = new() { Gap = Tokens.Number("DvSpace2"), Visibility = Visibility.Collapsed };

    private readonly Button attach = new();

    private readonly Select modelPick = new() { Label = ComposerViewModel.ModelLabel, Visibility = Visibility.Collapsed };

    private readonly Facet frame = new();

    private readonly Grid row = new() { ColumnSpacing = Tokens.Number("DvSpace2") };

    private readonly TravelLine travel = new();

    private readonly IconButton more = new() { Icon = IconName.Plus, Label = "More options", VerticalAlignment = VerticalAlignment.Bottom };

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

    private readonly TextBlock dropNote = Styles.Text("Drop the files to attach them.", "DvTextLabel");

    private static readonly string[] pictureFormats = ["image", "png", "jpeg", "bitmap", "dib", "tiff"];

    private ComposerViewModel? model;

    private bool fieldFocused;

    private bool focusWithin;

    private bool dropping;

    private bool? stacked;

    private bool focusWanted;

    #endregion

    #region Constructors

    public Composer()
    {
        MaxWidth = Tokens.Number("DvMeasure") + Tokens.Number("DvSpace6");

        Labelled(attach, IconName.Attach, "Attach");
        Labelled(stop, IconName.Stop, "Stop");
        Labelled(send, IconName.Send, "Send");
        AutomationProperties.SetName(options, "Message options");
        AutomationProperties.SetName(this, "Message composer");
        AutomationProperties.SetName(frame, "Write a message");
        AutomationProperties.SetAutomationId(field, "ComposerField");
        AutomationProperties.SetAutomationId(send, "ComposerSend");
        AutomationProperties.SetAutomationId(stop, "ComposerStop");
        AutomationProperties.SetAutomationId(attach, "ComposerAttach");
        AutomationProperties.SetAutomationId(more, "ComposerMore");
        options.Children.Add(attach);
        options.Children.Add(modelPick);

        BuildField();
        BuildRow();

        dropNote.Visibility = Visibility.Collapsed;
        column.Children.Add(tray);
        column.Children.Add(options);
        column.Children.Add(frame);
        column.Children.Add(dropNote);
        Children.Add(column);

        more.Click += (_, _) => model?.ToggleOptions();
        attach.Click += async (_, _) => await Pick();
        modelPick.RegisterPropertyChangedCallback(Select.ValueProperty, async (_, _) => await ChooseModel());
        stop.Click += async (_, _) => await (model?.Cancel() ?? Task.CompletedTask);
        send.Click += async (_, _) => await Send();
        tray.Remove += async (_, id) => await (model?.Remove(id) ?? Task.CompletedTask);

        Loaded += (_, _) =>
        {
            PlatformPaste.Start();
            PlatformPaste.Received += PastedInBrowser;
        };
        Unloaded += (_, _) => PlatformPaste.Received -= PastedInBrowser;

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
                model.OptionsFocusRequested -= FocusOptions;
            }

            model = value;

            if (model is not null)
            {
                model.PropertyChanged += Changed;
                model.FocusRequested += TakeFocus;
                model.OptionsFocusRequested += FocusOptions;
            }

            Show();
        }
    }

    #endregion

    #region Public Methods

    /// <summary>Puts the cursor in the message field. It waits its turn, so it comes after whatever moved the focus just now.</summary>
    public void TakeFocus()
    {
        focusWanted = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, FocusField);
    }

    /// <summary>The frame takes the ember edge and the line of words says it will take the files held over the conversation.</summary>
    public void ShowDropping(bool value)
    {
        dropping = value;
        dropNote.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        Paint();
    }

    /// <summary>Takes the files dropped on the conversation. What cannot be taken is said in one sentence.</summary>
    public async Task TakeDropped(Windows.ApplicationModel.DataTransfer.DataPackageView dropped)
    {
        ShowDropping(false);

        try
        {
            await Take(dropped);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.For<Composer>().LogError(exception, "What was dropped could not be read as files");
            model?.Notify(FileWords.DropFailed, ToastKind.Error);
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
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        row.Children.Add(more);
        row.Children.Add(fieldBox);
        row.Children.Add(stop);
        row.Children.Add(send);

        var pad = Tokens.Number("DvSpace2");
        frame.Padding = new(pad);
        travel.Margin = new(-pad, -pad, -pad, 0);
        frame.Children.Add(row);
        frame.Children.Add(travel);
    }

    // An icon and the word for what the button does. The icon takes the color the button's words have.
    private static void Labelled(Button button, IconName icon, string label)
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

        if (shown is null)
        {
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
        AutomationProperties.SetItemStatus(more, shown.OptionsOpen ? "Expanded" : "Collapsed");

        tray.BotName = shown.BotName;
        tray.Items = shown.Attachments;
        options.Visibility = shown.OptionsOpen ? Visibility.Visible : Visibility.Collapsed;
        attach.IsEnabled = !shown.Disabled;
        ShowModels(shown);
        stop.Visibility = shown.ShowsStop ? Visibility.Visible : Visibility.Collapsed;
        send.Visibility = shown.ShowsSend ? Visibility.Visible : Visibility.Collapsed;
        send.IsEnabled = shown.CanSend;
        travel.IsRunning = shown.IsWorking;
        Fit();
        Paint();
    }

    private void ShowModels(ComposerViewModel shown)
    {
        modelPick.Visibility = shown.HasModelChoice ? Visibility.Visible : Visibility.Collapsed;
        modelPick.Disabled = shown.Disabled;

        if (!modelPick.Options.Select(option => (option.Value, option.Label)).SequenceEqual(shown.ModelOptions.Select(option => (option.Value, option.Label))))
        {
            modelPick.Options = [.. shown.ModelOptions.Select(option => new SelectOption(option.Value, option.Label))];
        }

        modelPick.Value = shown.ChosenModel;
    }

    // A narrow window with Stop and Send side by side gives the field the whole first row and puts the two under it.
    private void Fit()
    {
        var phone = ActualWidth > 0 && ActualWidth < (double)Styles.Dictionary["DvBreakPhone"];
        var two = phone && (model?.HasTwoActions ?? false);
        var inline = Tokens.Number(phone ? "DvSpace2" : "DvSpace4");
        column.Padding = new(inline, Tokens.Number("DvSpace3"), inline, Tokens.Number("DvSpace4"));

        if (two == stacked)
        {
            return;
        }

        stacked = two;
        row.RowSpacing = two ? Tokens.Number("DvSpace2") : 0;
        SetColumn(fieldBox, 1);
        SetColumnSpan(fieldBox, two ? row.ColumnDefinitions.Count - 1 : 1);
        SetColumn(stop, 2);
        SetRow(stop, two ? 1 : 0);
        SetColumn(send, 3);
        SetRow(send, two ? 1 : 0);
    }

    private void Paint()
    {
        frame.Fill = Tokens.Brush(this, "DvBg1");
        frame.Edge = Tokens.Brush(this, focusWithin || dropping ? "DvEmber" : "DvText3");
        field.Foreground = Tokens.Brush(this, model?.Disabled is true ? "DvText3" : "DvText");
        placeholder.Foreground = Tokens.Brush(this, "DvText3");
        focusLine.Background = fieldFocused ? Tokens.Brush(this, "DvEmber") : null;
        dropNote.Foreground = Tokens.Brush(this, "DvText2");
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

    // Opening the row by keyboard puts focus on its first option, since the row comes before the plus button.
    private void FocusOptions() => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, attach.TakeFocus);

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

    private async Task ChooseModel()
    {
        if (model is not null)
        {
            await model.ChooseModel(modelPick.Value);
        }
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
        if (model is null)
        {
            return;
        }

        try
        {
            await Hand(await PickedFiles.Choose());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A device with no file chooser, or one that fails: the composer stays as it was, and the log says why.
            Log.For<Composer>().LogError(exception, "The device's file chooser could not be opened");
            model.Notify(FileWords.ChooserFailed, ToastKind.Error);
        }
    }

    // A copy from an office program carries its text and a picture of it: then the text is what was meant. In a
    // browser the page's paste event brings files and pictures instead, and the platform's clipboard holds text only.
    private async Task Pasted(TextControlPasteEventArgs pasted)
    {
        var carriesSomething = false;

        try
        {
            if (OperatingSystem.IsBrowser())
            {
                return;
            }

            var carried = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();

            if (carried.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                return;
            }

            if (Offers(carried))
            {
                carriesSomething = true;
                pasted.Handled = true;
                await Take(carried);
            }
            else if (carried.AvailableFormats.Any(IsPictureFormat))
            {
                // A picture is listed on the clipboard that this device did not hand over.
                model?.Notify(FileWords.PictureNotHandedOver, ToastKind.Warning);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.For<Composer>().LogError(exception, "What was pasted could not be read as files");

            if (carriesSomething)
            {
                model?.Notify(FileWords.PasteFailed, ToastKind.Error);
            }
        }
    }

    // The files of one paste the browser brought, together. They go to the composer whose field has the cursor.
    private async void PastedInBrowser(IReadOnlyList<IPickedFile> files)
    {
        if (fieldFocused)
        {
            await Hand(files);
        }
    }

    // A format a device lists for a picture it holds but did not hand over as a bitmap.
    private static bool IsPictureFormat(string format) =>
        pictureFormats.Any(word => format.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static bool Offers(Windows.ApplicationModel.DataTransfer.DataPackageView carried) =>
        carried.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)
        || carried.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap);

    // Files and pictures, whichever way they came, end here: files go on, and folders alone are said to be left out.
    private async Task Take(Windows.ApplicationModel.DataTransfer.DataPackageView carried)
    {
        var taken = await PickedFiles.From(carried);

        if (taken.Files.Count > 0)
        {
            await Hand(taken.Files);
        }
        else if (taken.Folders > 0)
        {
            model?.Notify(FileWords.FoldersOnly, ToastKind.Warning);
        }
    }

    private async Task Hand(IReadOnlyList<IPickedFile> files)
    {
        if (files.Count > 0 && model is not null)
        {
            await model.Chosen(files);
        }
    }

    #endregion
}
