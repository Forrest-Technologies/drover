namespace Drover.Native.Pages;

/// <summary>One section of the gallery: a title, a note, and the samples shown twice, once in each theme.</summary>
public sealed class GallerySection : StackPanel
{
    #region Private Fields

    private readonly Grid themes = new() { ColumnSpacing = Tokens.Number("DvSpace4"), RowSpacing = Tokens.Number("DvSpace4") };

    private readonly Border dark;

    private readonly Border light;

    private bool? sideBySide;

    #endregion

    #region Constructors

    /// <param name="title">What the section shows.</param>
    /// <param name="note">One line about how the component is meant to be used.</param>
    /// <param name="samples">Makes the samples. It runs twice, once for each theme.</param>
    public GallerySection(string title, string? note, Func<IEnumerable<UIElement>> samples)
    {
        Spacing = Tokens.Number("DvSpace3");
        Children.Add(Styles.Text(title, "DvTextH2"));
        if (note is not null)
        {
            Children.Add(Styles.Text(note, "DvTextSecondary"));
        }

        // The dark panel is drawn in the black colors while the black mode is in use.
        dark = Panel(ThemeTokens.Black ? "Black" : "Dark", ElementTheme.Dark, samples());
        light = Panel("Light", ElementTheme.Light, samples());
        themes.Children.Add(dark);
        themes.Children.Add(light);
        Children.Add(themes);
        SizeChanged += (_, _) => Fit();
    }

    #endregion

    #region Private Methods

    private static Border Panel(string name, ElementTheme theme, IEnumerable<UIElement> samples)
    {
        var flow = new FlowPanel { Gap = Tokens.Number("DvSpace4") };
        var label = Styles.Text(name, "DvTextMono");
        FlowPanel.SetFullRow(label, true);
        flow.Children.Add(label);
        foreach (var sample in samples)
        {
            flow.Children.Add(sample);
        }

        var panel = new Border
        {
            Child = flow,
            RequestedTheme = theme,
            Padding = new(Tokens.Number("DvSpace4")),
            BorderThickness = new(Tokens.Number("DvBorder")),
            CornerRadius = new(Tokens.Number("DvRadiusCard")),
        };
        panel.WhenShownOrRethemed(() =>
        {
            panel.Background = Tokens.Brush(panel, "DvBg0");
            panel.BorderBrush = Tokens.Brush(panel, "DvLine");
        });
        return panel;
    }

    // Side by side when both fit at their smallest, one over the other when they do not.
    private void Fit()
    {
        var smallest = Tokens.Number("DvSheetWidth") - Tokens.Number("DvSpace7");
        var fits = ActualWidth >= (2 * smallest) + Tokens.Number("DvSpace4");
        if (fits == sideBySide)
        {
            return;
        }

        sideBySide = fits;

        themes.ColumnDefinitions.Clear();
        themes.RowDefinitions.Clear();
        themes.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        themes.RowDefinitions.Add(new() { Height = GridLength.Auto });
        if (fits)
        {
            themes.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        }
        else
        {
            themes.RowDefinitions.Add(new() { Height = GridLength.Auto });
        }

        Grid.SetColumn(light, fits ? 1 : 0);
        Grid.SetRow(light, fits ? 0 : 1);
    }

    #endregion
}
