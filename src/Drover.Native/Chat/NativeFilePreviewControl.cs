namespace Drover.Native.Chat;

/// <summary>
/// Renders safe native file previews for Markdown, static PNG and JPEG images, PDF pages, and flowcharts without
/// invoking a web view or executing embedded content.
/// </summary>
internal sealed class NativeFilePreviewControl : StackPanel
{
    #region Private Fields

    private readonly NativeFilePreviewViewModel model = new();

    private readonly Button toggle = new() { Content = "Preview", HorizontalAlignment = HorizontalAlignment.Left };

    private readonly StatusNote status = new();

    private readonly MarkdownView markdown = new() { Visibility = Visibility.Collapsed };

    private readonly Image picture = new() { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };

    private readonly StackPanel pdfPanel = new() { Spacing = Tokens.Number("DvSpace2"), Visibility = Visibility.Collapsed };

    private readonly StackPanel pdfHeader = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = Tokens.Number("DvSpace2"),
    };

    private readonly Button previousPage = new() { Content = "Previous page" };

    private readonly Button nextPage = new() { Content = "Next page" };

    private readonly TextBlock pageCaption = Styles.Text(null, "DvTextMono");

    private readonly Border pdfCard = new()
    {
        Padding = new(Tokens.Number("DvSpace3")),
        BorderThickness = new(Tokens.Number("DvBorder")),
        CornerRadius = new(Tokens.Number("DvRadiusCard")),
    };

    private readonly StackPanel pdfPageBody = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly Image pdfPicture = new() { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };

    private readonly TextBlock pdfText = Styles.Text(null, "DvText14");

    private readonly StackPanel diagramHost = new() { Spacing = Tokens.Number("DvSpace2"), Visibility = Visibility.Collapsed };

    private byte[]? decodedPictureSource;

    private byte[]? decodedPdfPictureSource;

    #endregion

    #region Constructors

    public NativeFilePreviewControl(DroverApi api)
    {
        Spacing = Tokens.Number("DvSpace2");
        model.ReadPreview = (target, cancellationToken) => api.ReadNativePreview(target.Href, cancellationToken);

        toggle.MinHeight = Tokens.Number("DvTargetTouch");
        toggle.Click += async (_, _) => await model.Toggle();

        previousPage.MinHeight = Tokens.Number("DvTargetTouch");
        previousPage.Click += (_, _) => model.PreviousPage();
        AutomationProperties.SetName(previousPage, "Previous PDF page");

        nextPage.MinHeight = Tokens.Number("DvTargetTouch");
        nextPage.Click += (_, _) => model.NextPage();
        AutomationProperties.SetName(nextPage, "Next PDF page");

        pageCaption.VerticalAlignment = VerticalAlignment.Center;
        pdfHeader.Children.Add(previousPage);
        pdfHeader.Children.Add(nextPage);
        pdfHeader.Children.Add(pageCaption);

        pdfText.IsTextSelectionEnabled = true;
        pdfPageBody.Children.Add(pdfPicture);
        pdfPageBody.Children.Add(pdfText);
        pdfCard.Child = pdfPageBody;
        pdfPanel.Children.Add(pdfHeader);
        pdfPanel.Children.Add(pdfCard);

        Children.Add(toggle);
        Children.Add(status);
        Children.Add(markdown);
        Children.Add(picture);
        Children.Add(pdfPanel);
        Children.Add(diagramHost);

        model.PropertyChanged += (_, _) => Draw();
        Unloaded += (_, _) => model.Close();
        this.WhenShownOrRethemed(Draw);
    }

    #endregion

    #region Public Methods

    public void SetFile(SharedFile? value)
    {
        model.File = value;
        Draw();
    }

    internal static Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap? DecodePicture(
        byte[] bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length > NativeFilePreview.MaxImageBytes)
        {
            return null;
        }

        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg)
            || codec.FrameCount > 1 || (long)codec.Info.Width * codec.Info.Height > NativeFilePreview.MaxImagePixels)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var format = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap(format.Width, format.Height);
        using var pixels = new SKBitmap(format);
        var destination = pixels.GetPixels();
        if (destination == IntPtr.Zero
            || codec.GetPixels(format, destination) is not SKCodecResult.Success)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var stream = bitmap.PixelBuffer.AsStream();
        stream.Write(pixels.GetPixelSpan());
        bitmap.Invalidate();
        return bitmap;
    }

    internal static FrameworkElement BuildDiagram(FrameworkElement scope, NativeDiagram diagram)
    {
        var box = new Border
        {
            Padding = new(Tokens.Number("DvSpace3")),
            BorderThickness = new(Tokens.Number("DvBorder")),
            CornerRadius = new(Tokens.Number("DvRadiusCard")),
            Background = Tokens.Brush(scope, "DvBg2"),
            BorderBrush = Tokens.Brush(scope, "DvLine"),
        };
        var column = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        var labels = diagram.Nodes.ToDictionary(node => node.Id, node => node.Label, StringComparer.Ordinal);

        var flow = new FlowPanel { Gap = Tokens.Number("DvSpace2") };
        foreach (var node in diagram.Nodes)
        {
            var chipText = Styles.Text(node.Label);
            chipText.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
            var chip = new Border
            {
                Child = chipText,
                Padding = Tokens.Space("DvSpace3", "DvSpace1"),
                BorderThickness = new(Tokens.Number("DvBorder")),
                CornerRadius = new(Tokens.Number("DvRadiusControl")),
                Background = Tokens.Brush(scope, "DvBg1"),
                BorderBrush = Tokens.Brush(scope, "DvLine"),
            };
            AutomationProperties.SetName(chip, $"Diagram node {node.Label}");
            flow.Children.Add(chip);
        }

        column.Children.Add(flow);

        if (diagram.Edges.Count > 0)
        {
            var edges = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
            foreach (var edge in diagram.Edges)
            {
                var fromLabel = labels.GetValueOrDefault(edge.From, edge.From);
                var toLabel = labels.GetValueOrDefault(edge.To, edge.To);
                var description = string.IsNullOrWhiteSpace(edge.Label)
                    ? $"{fromLabel} -> {toLabel}"
                    : $"{fromLabel} -> {toLabel} ({edge.Label})";
                var line = Styles.Text(description, "DvTextMono");
                edges.Children.Add(line);
            }

            column.Children.Add(edges);
        }

        box.Child = column;
        AutomationProperties.SetName(box, "Diagram preview");
        return box;
    }

    #endregion

    #region Private Methods

    private void Draw()
    {
        if (!model.IsSupported)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        toggle.Content = model.ButtonLabel;
        AutomationProperties.SetName(toggle, model.ButtonName);
        AutomationProperties.SetName(picture, model.File?.Caption ?? model.File?.Name ?? string.Empty);

        status.Text = model.Status;

        if (model.MarkdownNodes.Count > 0)
        {
            markdown.ShowFile(model.MarkdownNodes);
            markdown.Visibility = Visibility.Visible;
        }
        else
        {
            markdown.Visibility = Visibility.Collapsed;
            markdown.ShowFile([]);
        }

        DrawPicture();
        DrawPdf();
        DrawDiagram();
    }

    private void DrawPicture()
    {
        if (model.ImageBytes is not { } bytes)
        {
            decodedPictureSource = null;
            picture.Source = null;
            picture.Visibility = Visibility.Collapsed;
            return;
        }

        if (!ReferenceEquals(decodedPictureSource, bytes))
        {
            decodedPictureSource = bytes;
            var bitmap = DecodePicture(bytes, CancellationToken.None);
            picture.Source = bitmap;
            picture.Visibility = bitmap is null ? Visibility.Collapsed : Visibility.Visible;
            if (bitmap is null)
            {
                model.FailImageDecode();
            }
        }
    }

    private void DrawPdf()
    {
        if (model.CurrentPdfPage is not { } page)
        {
            decodedPdfPictureSource = null;
            pdfPicture.Source = null;
            pdfPicture.Visibility = Visibility.Collapsed;
            pdfText.Text = string.Empty;
            pdfPanel.Visibility = Visibility.Collapsed;
            return;
        }

        pdfPanel.Visibility = Visibility.Visible;
        pdfCard.Background = Tokens.Brush(this, "DvBg2");
        pdfCard.BorderBrush = Tokens.Brush(this, "DvLine");
        pageCaption.Text = model.PageCaption ?? string.Empty;
        previousPage.IsEnabled = model.CanPreviousPage;
        nextPage.IsEnabled = model.CanNextPage;

        if (page.Image is { Length: > 0 } imageBytes)
        {
            if (!ReferenceEquals(decodedPdfPictureSource, imageBytes))
            {
                decodedPdfPictureSource = imageBytes;
                var bitmap = DecodePicture(imageBytes, CancellationToken.None);
                pdfPicture.Source = bitmap;
                pdfPicture.Visibility = bitmap is null ? Visibility.Collapsed : Visibility.Visible;
                AutomationProperties.SetName(pdfPicture, $"Page {page.Number}");
            }
        }
        else
        {
            decodedPdfPictureSource = null;
            pdfPicture.Source = null;
            pdfPicture.Visibility = Visibility.Collapsed;
        }

        pdfText.Text = page.Text ?? string.Empty;
        pdfText.Visibility = string.IsNullOrWhiteSpace(page.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void DrawDiagram()
    {
        diagramHost.Children.Clear();
        if (model.Diagram is not { } diagram)
        {
            diagramHost.Visibility = Visibility.Collapsed;
            return;
        }

        diagramHost.Children.Add(BuildDiagram(this, diagram));
        diagramHost.Visibility = Visibility.Visible;
    }

    #endregion
}
