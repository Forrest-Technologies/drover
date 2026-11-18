namespace Drover.Native.Pages;

/// <summary>How a <see cref="FlexPanel"/> spreads the free room of a line.</summary>
internal enum FlexJustify
{
    Start,
    End,
    SpaceBetween,
}

/// <summary>
/// A row that wraps, as the extensions screens lay out a header or a row of actions: items keep their own width,
/// a line takes as many as fit, and the free room of a line goes to the items that grow, or is spread by the
/// justification. An item can ask for a basis (the width it would like before it grows) and a grow factor. Items
/// are centered on their line, or sit at its top.
/// </summary>
internal sealed class FlexPanel : Panel
{
    #region Properties

    public static readonly DependencyProperty GapProperty = Dp.Register<FlexPanel, double>(nameof(Gap), 0, panel => panel.InvalidateMeasure());

    public static readonly DependencyProperty RowGapProperty = Dp.Register<FlexPanel, double>(nameof(RowGap), 0, panel => panel.InvalidateMeasure());

    public static readonly DependencyProperty JustifyProperty =
        Dp.Register<FlexPanel, FlexJustify>(nameof(Justify), FlexJustify.Start, panel => panel.InvalidateArrange());

    public static readonly DependencyProperty CenterItemsProperty = Dp.Register<FlexPanel, bool>(nameof(CenterItems), true, panel => panel.InvalidateArrange());

    public static readonly DependencyProperty BasisProperty =
        DependencyProperty.RegisterAttached("Basis", typeof(double), typeof(FlexPanel), new PropertyMetadata(double.NaN));

    public static readonly DependencyProperty GrowProperty =
        DependencyProperty.RegisterAttached("Grow", typeof(double), typeof(FlexPanel), new PropertyMetadata(0d));

    /// <summary>The space between items on a line: a spacing token.</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>The space between lines: a spacing token. The gap between items when not set.</summary>
    public double RowGap
    {
        get => (double)GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    public FlexJustify Justify
    {
        get => (FlexJustify)GetValue(JustifyProperty);
        set => SetValue(JustifyProperty, value);
    }

    /// <summary>Items sit in the middle of their line's height. They sit at its top when this is off.</summary>
    public bool CenterItems
    {
        get => (bool)GetValue(CenterItemsProperty);
        set => SetValue(CenterItemsProperty, value);
    }

    #endregion

    #region Public Methods

    public static double GetBasis(DependencyObject element) => (double)element.GetValue(BasisProperty);

    public static void SetBasis(DependencyObject element, double value) => element.SetValue(BasisProperty, value);

    public static double GetGrow(DependencyObject element) => (double)element.GetValue(GrowProperty);

    public static void SetGrow(DependencyObject element, double value) => element.SetValue(GrowProperty, value);

    #endregion

    #region Private Methods

    protected override Size MeasureOverride(Size availableSize)
    {
        var lines = Lay(availableSize.Width);
        var height = lines.Sum(line => line.Height) + (Math.Max(lines.Count - 1, 0) * Between);
        var width = lines.Count == 0 ? 0 : lines.Max(line => line.Used);
        return new(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0d;
        foreach (var line in Lay(finalSize.Width))
        {
            var (x, gap) = Start(line, finalSize.Width);
            foreach (var item in line.Items)
            {
                var top = y + (CenterItems ? (line.Height - item.Height) / 2 : 0);
                item.Child.Arrange(new(x, top, item.Width, item.Height));
                x += item.Width + gap;
            }

            y += line.Height + Between;
        }

        return finalSize;
    }

    private double Between => RowGap > 0 ? RowGap : Gap;

    // Where the first item of a line starts, and the space between its items.
    private (double X, double Gap) Start(Line line, double width)
    {
        var free = width - line.Used;
        return Justify switch
        {
            FlexJustify.End => (Math.Max(free, 0), Gap),
            FlexJustify.SpaceBetween when line.Items.Count > 1 && line.Grows == 0 => (0, Gap + (Math.Max(free, 0) / (line.Items.Count - 1))),
            _ => (0, Gap),
        };
    }

    // Breaks the items into lines for a width, shares the free room among the items that grow, and measures each
    // item at the width it ends up with, so text wraps as it will be drawn.
    private List<Line> Lay(double width)
    {
        List<Line> lines = [];
        var current = new Line();

        foreach (var child in Children.Where(child => child.Visibility is Visibility.Visible))
        {
            child.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            var basis = GetBasis(child);
            var wanted = double.IsNaN(basis) ? child.DesiredSize.Width : basis;
            var itemWidth = double.IsInfinity(width) ? wanted : Math.Min(wanted, width);

            if (current.Items.Count > 0 && !double.IsInfinity(width) && current.Used + Gap + itemWidth > width)
            {
                lines.Add(current);
                current = new();
            }

            current.Add(new(child, itemWidth), Gap);
        }

        if (current.Items.Count > 0)
        {
            lines.Add(current);
        }

        foreach (var line in lines)
        {
            Grow(line, width);

            foreach (var item in line.Items)
            {
                item.Child.Measure(new(item.Width, double.PositiveInfinity));
                item.Height = item.Child.DesiredSize.Height;
            }

            line.Height = line.Items.Count == 0 ? 0 : line.Items.Max(item => item.Height);
        }

        return lines;
    }

    private static void Grow(Line line, double width)
    {
        var free = width - line.Used;
        line.Grows = line.Items.Sum(item => GetGrow(item.Child));

        if (double.IsInfinity(width) || free <= 0 || line.Grows == 0)
        {
            return;
        }

        foreach (var item in line.Items)
        {
            item.Width += free * GetGrow(item.Child) / line.Grows;
        }

        line.Used = width;
    }

    private sealed class Item(UIElement child, double width)
    {
        public UIElement Child { get; } = child;

        public double Width { get; set; } = width;

        public double Height { get; set; }
    }

    private sealed class Line
    {
        #region Properties

        public List<Item> Items { get; } = [];

        public double Used { get; set; }

        public double Grows { get; set; }

        public double Height { get; set; }

        #endregion

        #region Public Methods

        public void Add(Item item, double gap)
        {
            Used += (Items.Count > 0 ? gap : 0) + item.Width;
            Items.Add(item);
        }

        #endregion
    }

    #endregion
}
