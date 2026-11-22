namespace Drover.Native.Chat;

/// <summary>
/// One file in a conversation. A picture is shown with its dimensions set; a text file shows its first lines, and all
/// of it on "Show all"; anything else is a card. Every form has the same way to get the file. A bot HTML file is shown
/// only in the browser's contained preview surface. A native head that cannot enforce that boundary keeps Download.
/// </summary>
public sealed class FileCard : Grid
{
    #region Private Fields

    private const int MaxPictureAttempts = 2;

    private readonly FileCardViewModel model;

    private readonly Batch batch;

    private readonly DroverApi api;

    private readonly StackPanel column = new() { Spacing = Tokens.Number("DvSpace2"), HorizontalAlignment = HorizontalAlignment.Left };

    private readonly OpensFile picture = new() { HoverFill = null, Radius = null, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left };

    private readonly Border pictureFrame = new();

    private readonly Grid pictureBody = new();

    private readonly Image image = new() { Stretch = Stretch.Uniform };

    private readonly TextBlock pictureProblem = Styles.Text(FileCardViewModel.PictureFailed, "DvTextSecondary");

    private readonly OutputBox preview = new();

    private readonly TextBlock cut = Styles.Text(null, "DvTextSecondary");

    private readonly Button showAll = new() { HorizontalAlignment = HorizontalAlignment.Left };

    private readonly StatusNote problem = new() { Kind = NoteKind.Danger };

    private readonly Facet card = new();

    private readonly TextBlock name = Styles.Text(null);

    private readonly TextBlock facts = Styles.Text(null, "DvTextMono");

    private readonly FlowPanel deviceActions = new() { Gap = Tokens.Number("DvSpace1"), VerticalAlignment = VerticalAlignment.Center };

    private readonly Button save = new() { Content = FileCardViewModel.Save };

    private readonly Button share = new() { Content = FileCardViewModel.Share };

    private readonly OpensFile download = new() { HoverFill = null, Radius = null, VerticalAlignment = VerticalAlignment.Center };

    private readonly StatusNote exported = new();

    private readonly Button openPreview = new() { Content = FileCardViewModel.PreviewFile, HorizontalAlignment = HorizontalAlignment.Left };

    private readonly StatusNote previewProblem = new() { Kind = NoteKind.Danger };

    private readonly HtmlPreview previewSurface = new();

    private readonly Dialog previewDialog = new();

    private readonly NativeFilePreviewControl nativePreview;

    private readonly TextBlock caption = Styles.Text(null, "DvTextSecondary");

    private Guid? loadedPicture;

    private Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap? pictureSource;

    private bool pictureFailed;

    private int pictureGeneration;

    private double room = double.NaN;

    #endregion

    #region Constructors

    public FileCard()
        : this(Pieces.Make<FileCardViewModel>())
    {
    }

    /// <summary>A card with a view model made elsewhere, for the gallery, whose samples have no server behind them.</summary>
    internal FileCard(FileCardViewModel model, DroverApi? reads = null)
    {
        this.model = model;
        api = reads ?? ((App)Application.Current).Services.GetRequiredService<DroverApi>();
        nativePreview = new(api);
        batch = new(this, Apply, Draw);

        // A file takes the width its content needs, up to the width of a picture and the room it is given, as the
        // web client's file does; it never takes its width from where it happens to sit.
        column.MaxWidth = Tokens.Number("DvImageWidth");

        pictureProblem.HorizontalAlignment = HorizontalAlignment.Center;
        pictureProblem.VerticalAlignment = VerticalAlignment.Center;
        pictureProblem.TextAlignment = TextAlignment.Center;
        pictureProblem.TextWrapping = TextWrapping.Wrap;
        pictureProblem.Margin = Tokens.Space("DvSpace3", "DvSpace3");
        pictureBody.Children.Add(image);
        pictureBody.Children.Add(pictureProblem);
        pictureFrame.Child = pictureBody;
        pictureFrame.BorderThickness = new(Tokens.Number("DvBorder"));
        pictureFrame.HorizontalAlignment = HorizontalAlignment.Left;
        picture.Content = pictureFrame;
        picture.Padding = new(0);
        picture.Click += (_, _) => OpenFile(toKeep: false);

        showAll.Click += async (_, _) => await model.ShowAll();
        save.Click += async (_, _) => await model.Export(save: true);
        share.Click += async (_, _) => await model.Export(save: false);
        deviceActions.Children.Add(save);
        deviceActions.Children.Add(share);

        var downloadWords = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        downloadWords.Children.Add(new Icon { NameOfIcon = IconName.Download, VerticalAlignment = VerticalAlignment.Center });
        downloadWords.Children.Add(new TextBlock { Text = FileCardViewModel.Download, VerticalAlignment = VerticalAlignment.Center });
        download.Content = downloadWords;
        download.Padding = new(0);
        download.MinHeight = Tokens.Number("DvTargetTouch");
        download.Click += (_, _) => OpenFile(toKeep: true);

        openPreview.Click += async (_, _) =>
        {
            if (OperatingSystem.IsBrowser())
            {
                await model.Preview();
            }
            else
            {
                model.RefusePreview();
            }
        };

        previewDialog.Body = previewSurface;
        previewDialog.RegisterPropertyChangedCallback(Dialog.OpenProperty, async (_, _) =>
        {
            if (!previewDialog.Open && model.PreviewAddress is not null)
            {
                previewSurface.Clear();
                await model.ClosePreview();
            }
        });

        name.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(name);
        words.Children.Add(facts);
        card.Cut = Tokens.Number("DvCutS");
        card.Padding = Tokens.Space("DvSpace3", "DvSpace2");
        card.ColumnSpacing = Tokens.Number("DvSpace3");
        card.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        card.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumn(deviceActions, 1);
        Grid.SetColumn(download, 1);
        card.Children.Add(words);
        card.Children.Add(deviceActions);
        card.Children.Add(download);

        column.Children.Add(picture);
        column.Children.Add(preview);
        column.Children.Add(cut);
        column.Children.Add(showAll);
        column.Children.Add(problem);
        column.Children.Add(card);
        column.Children.Add(exported);
        column.Children.Add(caption);
        column.Children.Add(openPreview);
        column.Children.Add(previewProblem);
        column.Children.Add(nativePreview);
        Children.Add(column);
        Children.Add(previewDialog);

        Unloaded += async (_, _) =>
        {
            previewSurface.Clear();
            await model.ClosePreview();
        };

        // Nothing shows until the file is drawn, so a card never appears empty with its buttons over each other.
        Visibility = Visibility.Collapsed;
        model.PropertyChanged += (_, _) => batch.Draw();
        this.WhenShownOrRethemed(batch.Draw);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty FileProperty = Dp.Register<FileCard, SharedFile?>(nameof(File), null, card => card.batch.Values());

    public static readonly DependencyProperty ReadWholeProperty =
        Dp.Register<FileCard, Func<SharedFile, Task<FileText>>?>(nameof(ReadWhole), null, card => card.batch.Values());

    /// <summary>The file to show. Required.</summary>
    public SharedFile? File
    {
        get => (SharedFile?)GetValue(FileProperty);
        set => SetValue(FileProperty, value);
    }

    /// <summary>Reads a text file's whole content, up to <see cref="FileWords.MaxShownCharacters"/>. Without it there is no "Show all".</summary>
    public Func<SharedFile, Task<FileText>>? ReadWhole
    {
        get => (Func<SharedFile, Task<FileText>>?)GetValue(ReadWholeProperty);
        set => SetValue(ReadWholeProperty, value);
    }

    #endregion

    #region Private Methods

    private void Apply()
    {
        if (model.File is { } before && before.Id != File?.Id)
        {
            previewDialog.Open = false;
            previewSurface.Clear();
            _ = model.ClosePreview();
        }

        if (File is { } file)
        {
            model.File = file;
            model.ReadWhole = ReadWhole;
            model.RequestPreview = selected => api.PreviewConversationFile(selected.Href);
            model.RevokePreview = (selected, grant) => api.RevokeConversationFilePreview(selected.Href, grant);
        }
    }

    private void Draw()
    {
        if (File is null || model.File is null)
        {
            ClearPicture();
            nativePreview.SetFile(null);
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        nativePreview.SetFile(model.File);
        DrawPicture();
        DrawText();

        card.Fill = Tokens.Brush(this, "DvBg2");
        name.Text = model.File.Name;
        facts.Text = model.Facts;
        deviceActions.Visibility = model.Delivers ? Visibility.Visible : Visibility.Collapsed;
        download.Visibility = model.Delivers ? Visibility.Collapsed : Visibility.Visible;
        // The pressed button stays enabled: disabling it would take keyboard and screen reader focus away from it. The
        // model ignores a press while it works, and the state is told to assistive technology instead.
        AutomationProperties.SetItemStatus(save, model.ExportingWords ?? string.Empty);
        AutomationProperties.SetItemStatus(share, model.ExportingWords ?? string.Empty);
        AutomationProperties.SetName(save, model.SaveName);
        AutomationProperties.SetName(share, model.ShareName);
        AutomationProperties.SetName(download, model.DownloadName);

        exported.Kind = model.ExportFailed ? NoteKind.Danger : NoteKind.Ok;
        exported.Text = model.ExportStatus;
        caption.Text = model.Caption ?? string.Empty;
        caption.Visibility = model.Caption is null ? Visibility.Collapsed : Visibility.Visible;

        openPreview.Visibility = model.CanPreview ? Visibility.Visible : Visibility.Collapsed;
        openPreview.Content = model.PreviewLabel;
        openPreview.IsEnabled = !model.Previewing;
        AutomationProperties.SetName(openPreview, model.PreviewButtonName);
        previewProblem.Text = model.PreviewProblem;
        ShowPreview();
    }


    private void ShowPreview()
    {
        if (File?.Id != model.File.Id || model.PreviewAddress is not { } address)
        {
            previewDialog.Open = false;
            return;
        }

        previewDialog.Title = $"Preview of {model.File.Name}";
        previewSurface.Show(address, model.File.Name);
        previewDialog.Open = true;
    }

    private void DrawPicture()
    {
        var shown = model.Kind is FileKind.Image;
        picture.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown)
        {
            ClearPicture();
            return;
        }

        pictureFrame.BorderBrush = Tokens.Brush(this, "DvLine");
        pictureFrame.Background = Tokens.Brush(this, "DvBg2");
        AutomationProperties.SetName(image, model.PictureName);
        KeepPictureShape();

        if (loadedPicture != model.File.Id)
        {
            pictureGeneration++;
            loadedPicture = model.File.Id;
            pictureSource = null;
            pictureFailed = false;
            LoadPicture(model.File, pictureGeneration, attempt: 1);
        }

        image.Source = pictureSource;
        image.Visibility = pictureSource is null ? Visibility.Collapsed : Visibility.Visible;
        pictureProblem.Visibility = pictureFailed ? Visibility.Visible : Visibility.Collapsed;
        if (pictureFailed)
        {
            (pictureFrame.Width, pictureFrame.Height) = (double.NaN, double.NaN);
        }

        var pictureName = pictureFailed ? $"{model.PictureName}. {FileCardViewModel.PictureFailed}" : model.PictureName;
        AutomationProperties.SetName(picture, pictureName);
        AutomationProperties.SetName(pictureFrame, pictureName);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        room = Math.Min(availableSize.Width, Tokens.Number("DvImageWidth"));
        KeepPictureShape();
        return base.MeasureOverride(availableSize);
    }

    // The frame has the picture's own shape from the start, so nothing below it moves when the picture arrives.
    private void KeepPictureShape()
    {
        if (pictureFailed || model.File is not { Width: > 0 and var wide, Height: > 0 and var high }
            || double.IsNaN(room) || room <= 0)
        {
            return;
        }

        var width = Math.Min(room, wide);
        var height = Math.Round(width * high / wide);
        if (double.IsNaN(pictureFrame.Width) || Math.Abs(pictureFrame.Width - width) >= 1 || Math.Abs(pictureFrame.Height - height) >= 1)
        {
            (pictureFrame.Width, pictureFrame.Height) = (width, height);
        }
    }

    // The server checks who asks for a file, so the picture is read through the same client every request uses.
    private async void LoadPicture(SharedFile file, int generation, int attempt)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var preview = await api.ReadNativePreview(file.Href, timeout.Token);
            if (preview.Refusal is NativePreviewRefusal.TooComplex
                && await RetryPicture(file, generation, attempt))
            {
                return;
            }

            var bitmap = preview is { Refusal: null, Image: { } bytes }
                ? NativeFilePreviewControl.DecodePicture(bytes, timeout.Token)
                : null;
            FinishPicture(file, generation, bitmap);
        }
        catch (OperationCanceledException exception)
        {
            Log.For<FileCard>().LogDebug(
                "A picture preview timed out on attempt {Attempt} with {ExceptionType}",
                attempt,
                exception.GetType().Name);
            if (!await RetryPicture(file, generation, attempt))
            {
                FinishPicture(file, generation, source: null);
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception)
            || exception is ArgumentException or IOException or InvalidOperationException)
        {
            Log.For<FileCard>().LogWarning(exception, "A picture in the conversation could not be read");
            FinishPicture(file, generation, source: null);
        }
    }

    private async Task<bool> RetryPicture(SharedFile file, int generation, int attempt)
    {
        if (attempt >= MaxPictureAttempts || !IsCurrentPicture(file, generation))
        {
            return false;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(Tokens.Number("DvDur3")));
        if (IsCurrentPicture(file, generation))
        {
            LoadPicture(file, generation, attempt + 1);
        }

        return true;
    }

    private void FinishPicture(
        SharedFile file,
        int generation,
        Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap? source)
    {
        if (!IsCurrentPicture(file, generation))
        {
            return;
        }

        pictureSource = source;
        pictureFailed = source is null;
        batch.Draw();
    }

    private bool IsCurrentPicture(SharedFile file, int generation) =>
        generation == pictureGeneration && loadedPicture == file.Id && model.File.Id == file.Id;

    private void ClearPicture()
    {
        if (loadedPicture is not null)
        {
            pictureGeneration++;
        }

        loadedPicture = null;
        pictureSource = null;
        pictureFailed = false;
        image.Source = null;
        image.Visibility = Visibility.Collapsed;
        pictureProblem.Visibility = Visibility.Collapsed;
    }

    private void DrawText()
    {
        var shown = model.Kind is FileKind.Text;
        preview.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        cut.Visibility = shown && model.CutNote is not null ? Visibility.Visible : Visibility.Collapsed;
        showAll.Visibility = shown && model.CutNote is null && model.CanShowAll ? Visibility.Visible : Visibility.Collapsed;
        problem.Text = shown ? model.Problem : null;
        if (!shown)
        {
            return;
        }

        preview.Label = model.PreviewName;
        preview.Show(model.PreviewText);
        cut.Text = model.CutNote ?? string.Empty;
        showAll.Content = model.ShowAllLabel;
        AutomationProperties.SetName(showAll, model.ShowAllName);

        // The button goes away once the text is in, so focus moves to the text it brought.
        if (model.TakeFocusForText())
        {
            preview.TakeFocus();
        }
    }

    // In a browser the picture opens as the web client's link opens it, and "Download" asks the server for the file as
    // one to keep, which is what the web client's download link does.
    private void OpenFile(bool toKeep)
    {
        if (model.File is not { } file || !Uri.TryCreate(file.Href, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
        {
            return;
        }

        var asked = toKeep && address.Query.Length is 0 ? new Uri($"{address}?download=true") : address;
        _ = Launcher.LaunchUriAsync(asked);
    }

    #endregion

    #region Nested Types

    // To assistive technology the picture and "Download" are links: they lead to the file.
    private sealed class OpensFile : PlainButton
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new RoleButtonPeer(this, AutomationControlType.Hyperlink);
    }

    #endregion
}
