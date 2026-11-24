namespace Drover.Native.Controls;

/// <summary>
/// Message text, drawn from the nodes <see cref="MarkdownDocument"/> makes of it. Only those node kinds reach the
/// screen, and each is drawn as text and native controls: nothing in a message is ever treated as markup here, so
/// there is nothing for a message to inject. A remote image is not fetched until a person asks for it.
/// </summary>
public sealed class MarkdownView : StackPanel
{
    #region Private Fields

    private static readonly HashSet<string> blocks = ["p", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "blockquote", "hr", "table"];

    private readonly HashSet<Uri> loadedImages = [];

    private readonly Dictionary<CodeNode, NativeDiagram> diagrams = [];

    private string? parsedText;

    private IReadOnlyList<MarkdownNode> nodes = [];

    // What the last paragraph of a streaming reply said when it was last drawn, and when each stretch of it that
    // is still fading in arrived.
    private readonly List<(int Start, long At)> arrivals = [];

    private string arrived = string.Empty;

    #endregion

    #region Constructors

    public MarkdownView()
    {
        Spacing = Tokens.Number("DvSpace3");
        this.WhenShownOrRethemed(Draw);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty TextProperty = Dp.Register<MarkdownView, string>(nameof(Text), string.Empty, view => view.Parse());

    public static readonly DependencyProperty StreamingProperty = Dp.Register<MarkdownView, bool>(nameof(Streaming), false, view => view.Draw());

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>True while the text is still arriving. What arrives fades in, and assistive technology is told to wait.</summary>
    public bool Streaming
    {
        get => (bool)GetValue(StreamingProperty);
        set => SetValue(StreamingProperty, value);
    }

    #endregion

    #region File Preview

    internal void ShowFile(IReadOnlyList<MarkdownNode> content)
    {
        nodes = content;
        diagrams.Clear();
        CollectDiagrams(content);
        Draw();
    }

    private void CollectDiagrams(IReadOnlyList<MarkdownNode> items)
    {
        foreach (var item in items)
        {
            if (item is CodeNode code
                && NativeDiagramParser.IsDiagramLanguage(code.Language)
                && code.Code.Length <= NativeFilePreview.MaxTextBytes
                && NativeDiagramParser.TryParse(code.Code, out var diagram, out _)
                && diagram is not null)
            {
                diagrams[code] = diagram;
            }
            else if (item is ElementNode element)
            {
                CollectDiagrams(element.Children);
            }
        }
    }

    #endregion

    #region Parsing

    private void Parse()
    {
        if (string.Equals(Text, parsedText, StringComparison.Ordinal))
        {
            return;
        }

        diagrams.Clear();
        nodes = MarkdownDocument.Parse(Text);
        parsedText = Text;
        Draw();
    }

    #endregion

    #region Blocks

    private void Draw()
    {
        var before = Children.Count;
        Children.Clear();
        AddBlocks(this, nodes, "DvText");
        AutomationProperties.SetItemStatus(this, Streaming ? "Busy" : string.Empty);

        // Text is streaming: the block that just arrived fades in, and so do the characters added to the last one.
        if (Streaming && Children.Count > before && Children.Count > 0)
        {
            Motion.Arrive(Children[^1]);
            arrived = PlainText(LastParagraph(this));
            arrivals.Clear();
        }
        else if (Streaming)
        {
            FadeNewCharacters(LastParagraph(this));
        }
        else
        {
            arrived = string.Empty;
            arrivals.Clear();
        }
    }

    #endregion

    #region Streaming

    private static TextBlock? LastParagraph(Panel panel) => panel.Children.LastOrDefault() switch
    {
        TextBlock paragraph => paragraph,
        Grid { Children: [.., Panel content] } => LastParagraph(content),
        Panel inner => LastParagraph(inner),
        _ => null,
    };

    private static string PlainText(TextBlock? paragraph) =>
        paragraph is null ? string.Empty : string.Concat(paragraph.Inlines.OfType<Microsoft.UI.Xaml.Documents.Run>().Select(run => run.Text));

    // New characters fade in over the shortest duration. The paragraph is drawn again each time text arrives, so a
    // stretch that arrived a moment ago carries on from how far its fade had come. With reduced motion, and when
    // the text did not simply grow, the characters are just there.
    private void FadeNewCharacters(TextBlock? paragraph)
    {
        var now = Environment.TickCount64;
        var lasts = (long)Motion.Duration("DvDur1").TotalMilliseconds;
        var text = PlainText(paragraph);
        var grew = text.Length > arrived.Length && text.StartsWith(arrived, StringComparison.Ordinal);

        arrivals.RemoveAll(stretch => now - stretch.At >= lasts || stretch.Start >= text.Length);
        if (!grew && text != arrived)
        {
            arrivals.Clear();
        }
        else if (grew)
        {
            arrivals.Add((arrived.Length, now));
        }

        arrived = text;

        if (paragraph is null || lasts <= 0 || paragraph.Inlines.Any(inline => inline is not Microsoft.UI.Xaml.Documents.Run))
        {
            return;
        }

        for (var index = arrivals.Count - 1; index >= 0; index--)
        {
            var (start, at) = arrivals[index];
            var end = index == arrivals.Count - 1 ? text.Length : arrivals[index + 1].Start;
            Fade(paragraph, start, end, (double)(now - at) / lasts, TimeSpan.FromMilliseconds(lasts - (now - at)));
        }
    }

    // The runs between two places in the paragraph get an ink of their own, which comes up from how far it had come.
    private static void Fade(TextBlock paragraph, int start, int end, double from, TimeSpan remaining)
    {
        var ink = new SolidColorBrush(((SolidColorBrush)paragraph.Foreground).Color) { Opacity = from };
        var position = 0;

        for (var index = 0; index < paragraph.Inlines.Count; index++)
        {
            var run = (Microsoft.UI.Xaml.Documents.Run)paragraph.Inlines[index];
            var (runStart, runEnd) = (position, position + run.Text.Length);
            position = runEnd;

            if (runEnd <= start || runStart >= end)
            {
                continue;
            }

            if (runStart < start)
            {
                paragraph.Inlines.Insert(index + 1, Like(run, run.Text[(start - runStart)..]));
                run.Text = run.Text[..(start - runStart)];
                position = start;
                continue;
            }

            if (runEnd > end)
            {
                paragraph.Inlines.Insert(index + 1, Like(run, run.Text[(end - runStart)..]));
                run.Text = run.Text[..(end - runStart)];
                position = end;
            }

            run.Foreground = ink;
        }

        var fade = new DoubleAnimation { From = from, To = 1, Duration = remaining };
        Storyboard.SetTarget(fade, ink);
        Storyboard.SetTargetProperty(fade, nameof(Brush.Opacity));
        var once = new Storyboard();
        once.Children.Add(fade);
        once.Begin();
    }

    private static Microsoft.UI.Xaml.Documents.Run Like(Microsoft.UI.Xaml.Documents.Run run, string text) => new()
    {
        Text = text,
        FontWeight = run.FontWeight,
        FontStyle = run.FontStyle,
        TextDecorations = run.TextDecorations,
        FontFamily = run.FontFamily,
        FontSize = run.FontSize,
    };

    #endregion

    #region Laying Out Blocks

    // Inline nodes between blocks are gathered into one paragraph, as loose text in a list item is.
    private void AddBlocks(Panel target, IReadOnlyList<MarkdownNode> children, string ink)
    {
        List<MarkdownNode> loose = [];

        foreach (var node in children)
        {
            if (node is CodeNode || (node is ElementNode element && blocks.Contains(element.Name)))
            {
                Flush();
                foreach (var block in Block(node, ink))
                {
                    target.Children.Add(block);
                }
            }
            else
            {
                loose.Add(node);
            }
        }

        Flush();

        void Flush()
        {
            foreach (var block in Prose(loose, ink))
            {
                target.Children.Add(block);
            }

            loose.Clear();
        }
    }

    private IEnumerable<FrameworkElement> Block(MarkdownNode node, string ink) => node switch
    {
        CodeNode code when diagrams.TryGetValue(code, out var diagram) =>
            [NativeFilePreviewControl.BuildDiagram(this, diagram)],
        CodeNode code => [new CodeBlock { PlainLabel = CodeContent.CodeLabel, Code = code.Code, CodeLanguage = code.Language }],
        ElementNode { Name: "h1" } heading => [Paragraph(heading.Children, ink, "DvText20", heading: true)],
        ElementNode { Name: "h2" } heading => [Paragraph(heading.Children, ink, "DvText16", heading: true)],
        ElementNode { Name: "h3" or "h4" or "h5" or "h6" } heading => [Paragraph(heading.Children, ink, "DvText14", heading: true)],
        ElementNode { Name: "ul" or "ol" } list => [List(list, ink)],
        ElementNode { Name: "blockquote" } quote => [Quote(quote)],
        ElementNode { Name: "hr" } => [new Border { Height = Tokens.Number("DvBorder"), Background = Tokens.Brush(this, "DvLine") }],
        ElementNode { Name: "table" } table => [Table(table, ink)],
        ElementNode element => Prose(element.Children, ink),
        _ => Prose([node], ink),
    };

    // A run of text is one paragraph. A picture cannot sit inside a line of text here, so each stands on its own
    // under the words it came with.
    private IEnumerable<FrameworkElement> Prose(IReadOnlyList<MarkdownNode> children, string ink)
    {
        if (children.Any(node => node is not ImageNode && (node is not TextNode { Text: var text } || !string.IsNullOrWhiteSpace(text))))
        {
            yield return Paragraph(children, ink, "DvText14", heading: false);
        }

        foreach (var image in children.OfType<ImageNode>())
        {
            yield return Picture(image);
        }
    }

    private StackPanel List(ElementNode list, string ink)
    {
        var rows = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
        var number = list.Start ?? 1;

        foreach (var item in list.Children.OfType<ElementNode>().Where(child => child.Name == "li"))
        {
            var task = item.Children.OfType<TaskNode>().FirstOrDefault();
            var row = new Grid();
            row.ColumnDefinitions.Add(new() { Width = new(Tokens.Number("DvSpace5")) });
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            row.Children.Add(task is null ? Marker(list.Name == "ol" ? $"{number++}." : "•", ink) : TaskMark(task));

            var content = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
            AddBlocks(content, [.. item.Children.Where(child => child is not TaskNode)], ink);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            rows.Children.Add(row);
        }

        return rows;
    }

    private TextBlock Marker(string text, string ink)
    {
        var marker = Styles.Text(text);
        marker.Foreground = Tokens.Brush(this, ink);
        marker.HorizontalAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetAccessibilityView(marker, AccessibilityView.Raw);
        return marker;
    }

    // The checkbox of a task list item: a small box, filled and ticked when done, named in words. It cannot be
    // changed, so it is not a control.
    private Border TaskMark(TaskNode task)
    {
        var size = Tokens.Number("DvText13");
        var box = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new(Tokens.Number("DvFocusWidth")),
            BorderThickness = new(task.Done ? 0 : Tokens.Number("DvBorder")),
            BorderBrush = Tokens.Brush(this, "DvText3"),
            Background = Tokens.Brush(this, task.Done ? "DvText2" : "DvBg3"),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new(0, ((Tokens.Number("DvText14") * Tokens.Number("DvLeadingText")) - size) / 2, 0, 0),
            Child = task.Done ? new Icon { NameOfIcon = IconName.Check, Width = size, Height = size, Foreground = Tokens.Brush(this, "DvBg0") } : null,
        };
        AutomationProperties.SetName(box, task.Done ? "Done" : "Not done");
        return box;
    }

    private Border Quote(ElementNode quote)
    {
        var inside = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        AddBlocks(inside, quote.Children, "DvText2");

        return new()
        {
            Child = inside,
            Padding = new(Tokens.Number("DvSpace3"), 0, 0, 0),
            BorderThickness = new(Tokens.Number("DvFocusWidth"), 0, 0, 0),
            BorderBrush = Tokens.Brush(this, "DvLine"),
        };
    }

    // A wide table scrolls sideways inside its own box, which takes the focus so the keyboard can scroll it.
    private ScrollViewer Table(ElementNode table, string ink)
    {
        var grid = new Grid();
        var rows = table.Children.OfType<ElementNode>()
            .SelectMany(section => section.Name == "tr" ? [section] : section.Children.OfType<ElementNode>().Where(row => row.Name == "tr"))
            .ToList();

        foreach (var (row, rowIndex) in rows.Select((row, index) => (row, index)))
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var cells = row.Children.OfType<ElementNode>().Where(cell => cell.Name is "th" or "td").ToList();

            foreach (var (cell, columnIndex) in cells.Select((cell, index) => (cell, index)))
            {
                while (grid.ColumnDefinitions.Count <= columnIndex)
                {
                    grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                }

                var isHeader = cell.Name == "th";
                var words = Paragraph(cell.Children, isHeader ? "DvText2" : ink, "DvText14", heading: false);
                words.TextWrapping = TextWrapping.NoWrap;
                if (isHeader)
                {
                    words.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
                }

                var box = new Border
                {
                    Child = words,
                    Padding = Tokens.Space("DvSpace3", "DvSpace2"),
                    BorderThickness = new(0, 0, 0, Tokens.Number("DvBorder")),
                    BorderBrush = Tokens.Brush(this, "DvLine"),
                };
                Grid.SetRow(box, rowIndex);
                Grid.SetColumn(box, columnIndex);
                grid.Children.Add(box);
            }
        }

        var scroll = new ScrollViewer
        {
            Content = grid,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsTabStop = true,
        };
        AutomationProperties.SetName(scroll, "Table");
        return scroll;
    }

    #endregion

    #region Inlines

    private TextBlock Paragraph(IReadOnlyList<MarkdownNode> children, string ink, string size, bool heading)
    {
        var paragraph = Styles.Text(null);
        paragraph.IsTextSelectionEnabled = true;
        paragraph.FontSize = Tokens.Number(size);
        paragraph.Foreground = Tokens.Brush(this, ink);

        // Conversation text is set in the theme's reading face, and its headings in the heading face.
        paragraph.FontFamily = heading ? Tokens.FontHeading : Tokens.FontReading;

        if (heading)
        {
            paragraph.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightHeading"];
            paragraph.LineHeight = Tokens.Number(size) * Tokens.Number("DvLeadingHeading");
            AutomationProperties.SetHeadingLevel(paragraph, AutomationHeadingLevel.Level3);
        }

        AddInlines(paragraph.Inlines, children, new(false, false, false, false));
        MarkMachineText(paragraph);
        return paragraph;
    }

    // Inline code sits on the card surface, so what the machine said looks different from what was said.
    private void MarkMachineText(TextBlock paragraph)
    {
        var surface = new Microsoft.UI.Xaml.Documents.TextHighlighter { Background = Tokens.Brush(this, "DvBg2"), Foreground = paragraph.Foreground };
        var position = 0;
        Walk(paragraph.Inlines);

        if (surface.Ranges.Count > 0)
        {
            paragraph.TextHighlighters.Add(surface);
        }

        void Walk(Microsoft.UI.Xaml.Documents.InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Microsoft.UI.Xaml.Documents.Run run:
                        if (ReferenceEquals(run.FontFamily, Tokens.FontMono))
                        {
                            surface.Ranges.Add(new() { StartIndex = position, Length = run.Text.Length });
                        }

                        position += run.Text.Length;
                        break;
                    case Microsoft.UI.Xaml.Documents.LineBreak:
                        position++;
                        break;
                    case Microsoft.UI.Xaml.Documents.Span span:
                        Walk(span.Inlines);
                        break;
                }
            }
        }
    }

    private void AddInlines(Microsoft.UI.Xaml.Documents.InlineCollection target, IReadOnlyList<MarkdownNode> children, InlineStyle style)
    {
        foreach (var node in children)
        {
            switch (node)
            {
                case TextNode text:
                    target.Add(Run(text.Text, style));
                    break;
                case ElementNode { Name: "br" }:
                    target.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
                    break;
                case ElementNode { Name: "strong" } strong:
                    AddInlines(target, strong.Children, style with { Strong = true });
                    break;
                case ElementNode { Name: "em" } emphasis:
                    AddInlines(target, emphasis.Children, style with { Emphasis = true });
                    break;
                case ElementNode { Name: "del" } removed:
                    AddInlines(target, removed.Children, style with { Struck = true });
                    break;
                case ElementNode { Name: "code" } code:
                    AddInlines(target, code.Children, style with { Machine = true });
                    break;
                case ElementNode element:
                    AddInlines(target, element.Children, style);
                    break;
                case LinkNode link:
                    target.Add(Hyperlink(link, style));
                    break;
                case ImageNode:
                    break;
                case TaskNode task:
                    target.Add(Run(task.Done ? "Done: " : "Not done: ", style));
                    break;
            }
        }
    }

    private Microsoft.UI.Xaml.Documents.Run Run(string text, InlineStyle style)
    {
        var run = new Microsoft.UI.Xaml.Documents.Run { Text = text };

        if (style.Strong)
        {
            run.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightHeading"];
        }

        if (style.Emphasis)
        {
            run.FontStyle = Windows.UI.Text.FontStyle.Italic;
        }

        if (style.Struck)
        {
            run.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
        }

        if (style.Machine)
        {
            run.FontFamily = Tokens.FontMono;
            run.FontSize = Tokens.Number("DvText13");
        }

        return run;
    }

    // A link opens outside the app, as a link in a message opens in a new tab on the web. Only absolute http,
    // https, and mailto addresses become links at all; the parser leaves the rest as text.
    private Microsoft.UI.Xaml.Documents.Hyperlink Hyperlink(LinkNode link, InlineStyle style)
    {
        var hyperlink = new Microsoft.UI.Xaml.Documents.Hyperlink
        {
            NavigateUri = new Uri(link.Address),
            Foreground = Tokens.Brush(this, "DvEmber"),
        };
        AddInlines(hyperlink.Inlines, link.Children, style);
        return hyperlink;
    }

    #endregion

    #region Remote Images

    private FrameworkElement Picture(ImageNode image)
    {
        if (!loadedImages.Contains(image.Source))
        {
            var load = new PlainButton
            {
                Content = PlaceholderLabel(image),
                Fill = "DvBg2",
                Line = "DvText3",
                Padding = Tokens.Space("DvSpace3", "DvSpace1"),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"],
            };
            load.HorizontalAlignment = HorizontalAlignment.Left;
            load.Click += (_, _) =>
            {
                loadedImages.Add(image.Source);
                Draw();
            };
            return load;
        }

        // The frame keeps one shape whatever the picture's own size is, so nothing below it moves when it arrives.
        var width = Tokens.Number("DvImageWidth");
        var picture = new Image
        {
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(image.Source),
            Stretch = Stretch.Uniform,
        };
        AutomationProperties.SetName(picture, image.Alt);

        return new Border
        {
            Child = picture,
            MaxWidth = width,
            Height = width / Tokens.Number("DvImageRatio"),
            CornerRadius = new(Tokens.Number("DvRadiusControl")),
            Background = Tokens.Brush(this, "DvBg2"),
        };
    }

    private static string PlaceholderLabel(ImageNode image) =>
        string.IsNullOrWhiteSpace(image.Alt)
            ? $"Load image from {image.Source.Host}"
            : $"Load image from {image.Source.Host}: {image.Alt}";

    private sealed record InlineStyle(bool Strong, bool Emphasis, bool Struck, bool Machine);

    #endregion
}
