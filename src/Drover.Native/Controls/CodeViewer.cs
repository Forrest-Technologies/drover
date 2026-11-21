namespace Drover.Native.Controls;

/// <summary>
/// A block of code or output at the size of the window, over the app and its dim: every line, scrolling both ways,
/// and a table's header row kept in view while its rows scroll under it. The header names the block and holds the
/// wrap toggle, the copy icon, and the close icon. It is a dialog to assistive technology, named "Full screen:" and the
/// block's kind; the focus moves into it and stays there until it closes by its close icon, Escape, or a device's
/// Back, and then goes back to the button that opened it. It opens on the block's wrap choice, and a change here is
/// its own. With reduced motion it appears without fading in.
/// </summary>
internal sealed class CodeViewer
{
    #region Private Fields

    private static CodeViewer? shared;

    private readonly CodeViewerModel model = new(DialogSurface.Navigator);

    private readonly Popup popup = new() { IsLightDismissEnabled = false };

    private readonly Grid layer = new();

    private readonly Grid safeArea = new();

    private readonly Border scrim = new();

    private readonly Border panel = new();

    private readonly Border header = new();

    private readonly TextBlock title = Styles.Text(null, "DvTextLabel");

    private readonly IconButton wrap = new() { Icon = IconName.Wrap, Label = CodeContent.WrapLabel, Ink = "DvText2", Pressed = false };

    private readonly CopyButton copy = new() { Label = OutputWords.CopyCode, Ink = "DvText2", VerticalAlignment = VerticalAlignment.Center };

    private readonly IconButton close = new() { Icon = IconName.Close, Label = CodeViewerModel.CloseLabel, Ink = "DvText2" };

    // A table's header row scrolls sideways with its rows, and never down.
    private readonly ScrollViewer heading = new()
    {
        HorizontalScrollMode = ScrollMode.Disabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        VerticalScrollMode = ScrollMode.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        IsTabStop = false,
    };

    private readonly ScrollViewer scroll = new()
    {
        HorizontalScrollMode = ScrollMode.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollMode = ScrollMode.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        IsTabStop = true,
        UseSystemFocusVisuals = true,
    };

    private FrameworkElement? owner;

    private Control? opener;

    #endregion

    #region Constructors

    private CodeViewer()
    {
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        copy.Text = () => model.Content?.Source ?? string.Empty;
        wrap.Click += (_, _) => model.ToggleWrap();
        close.Click += (_, _) => model.Close();
        model.Closed += Hide;
        model.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName is nameof(CodeViewerModel.Wraps) && popup.IsOpen)
            {
                Draw();
            }
        };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(wrap);
        actions.Children.Add(copy);
        actions.Children.Add(close);

        var top = new Grid { ColumnSpacing = Tokens.Number("DvSpace2") };
        top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumn(actions, 1);
        top.Children.Add(title);
        top.Children.Add(actions);
        header.Child = top;
        header.Padding = new(Tokens.Number("DvSpace4"), Tokens.Number("DvSpace1"), Tokens.Number("DvSpace2"), Tokens.Number("DvSpace1"));
        header.BorderThickness = new(0, 0, 0, Tokens.Number("DvBorder"));

        heading.Padding = new(Tokens.Number("DvSpace4"), Tokens.Number("DvSpace3"), Tokens.Number("DvSpace4"), 0);
        scroll.Padding = new(Tokens.Number("DvSpace4"));
        scroll.FocusVisualPrimaryThickness = new(Tokens.Number("DvFocusWidth"));
        scroll.FocusVisualSecondaryThickness = new(0);
        scroll.ViewChanged += (_, _) => heading.ChangeView(scroll.HorizontalOffset, null, null, disableAnimation: true);

        var column = new Grid();
        column.RowDefinitions.Add(new() { Height = GridLength.Auto });
        column.RowDefinitions.Add(new() { Height = GridLength.Auto });
        column.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        Grid.SetRow(heading, 1);
        Grid.SetRow(scroll, 2);
        column.Children.Add(header);
        column.Children.Add(heading);
        column.Children.Add(scroll);

        panel.Child = column;
        panel.BorderThickness = new(Tokens.Number("DvBorder"));
        panel.TabFocusNavigation = KeyboardNavigationMode.Cycle;
        panel.KeyDown += (_, key) => CloseOnEscape(key);
        layer.Children.Add(scrim);
        layer.Children.Add(safeArea);
        safeArea.Children.Add(panel);
#if ANDROID
        // Popups are siblings of the shell, so they also need to respect the window's visible bounds.
        SafeArea.Attach(safeArea);
#endif
        popup.Child = layer;
    }

    #endregion

    #region Public Methods

    /// <summary>Opens a block full screen over <paramref name="from"/>'s window. The focus returns to <paramref name="openedBy"/>.</summary>
    public static void Open(FrameworkElement from, CodeContent content, bool wraps, string copyLabel, Control openedBy) =>
        (shared ??= new()).Show(from, content, wraps, copyLabel, openedBy);

    #endregion

    #region Private Methods

    private void Show(FrameworkElement from, CodeContent content, bool wraps, string copyLabel, Control openedBy)
    {
        if (from.XamlRoot is not { } root)
        {
            return;
        }

        (owner, opener) = (from, openedBy);
        copy.Label = copyLabel;
        model.Show(content, wraps);
        Draw();

        if (!popup.IsOpen)
        {
            popup.XamlRoot = root;
            root.Changed += Resize;
            Place(root.Size);
            popup.IsOpen = true;
            Motion.FadeIn(layer);
            scroll.Loaded += FocusBody;
            scroll.Focus(FocusState.Keyboard);
        }
    }

    private void Hide()
    {
        if (!popup.IsOpen)
        {
            return;
        }

        popup.IsOpen = false;
        if (popup.XamlRoot is { } root)
        {
            root.Changed -= Resize;
        }

        scroll.Content = null;
        heading.Content = null;
        opener?.Focus(FocusState.Keyboard);
        (owner, opener) = (null, null);
    }

    private void FocusBody(object sender, RoutedEventArgs args)
    {
        scroll.Loaded -= FocusBody;
        scroll.Focus(FocusState.Keyboard);
    }

    private void CloseOnEscape(KeyRoutedEventArgs key)
    {
        if (key.Key is VirtualKey.Escape)
        {
            key.Handled = true;
            model.Close();
        }
    }

    private void Resize(XamlRoot root, XamlRootChangedEventArgs args) => Place(root.Size);

    private void Place(Size window)
    {
        layer.Width = window.Width;
        layer.Height = window.Height;
    }

    private void Draw()
    {
        if (model.Content is not { } held)
        {
            return;
        }

        var shown = CodeViewerModel.Viewed(held);

        layer.RequestedTheme = owner?.ActualTheme ?? ElementTheme.Default;
        scrim.Background = Tokens.Brush(layer, "DvScrim");
        panel.Background = Tokens.Brush(layer, "DvBg2");
        panel.BorderBrush = Tokens.Brush(layer, "DvLine");
        header.BorderBrush = Tokens.Brush(layer, "DvLine");
        title.Foreground = Tokens.Brush(layer, "DvText");
        scroll.FocusVisualPrimaryBrush = Tokens.Brush(layer, "DvEmber");

        title.Text = shown.Label;
        AutomationProperties.SetName(panel, model.Title);
        AutomationProperties.SetName(scroll, shown.Label);

        var isText = shown.Kind is not (CodeKind.Table or CodeKind.Binary or CodeKind.Bytes);
        var wrapped = isText && model.Wraps;
        wrap.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        wrap.Pressed = model.Wraps;
        scroll.HorizontalScrollMode = wrapped ? ScrollMode.Disabled : ScrollMode.Auto;
        scroll.HorizontalScrollBarVisibility = wrapped ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;

        var drawing = new CodeDrawing(layer);

        if (shown is { Kind: CodeKind.Table, Table: { } table })
        {
            var (names, rows) = drawing.SplitTable(table);
            heading.Content = names;
            heading.Visibility = names is null ? Visibility.Collapsed : Visibility.Visible;
            scroll.Content = rows;
            return;
        }

        heading.Content = null;
        heading.Visibility = Visibility.Collapsed;
        scroll.Content = drawing.Body(shown, model.Wraps, all: true, CodeBytesView.Hex).Drawn;
    }

    #endregion
}
