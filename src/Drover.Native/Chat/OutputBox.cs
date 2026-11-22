namespace Drover.Native.Chat;

/// <summary>
/// Text a machine wrote, in a box that scrolls: what a tool printed, a command with its output, a file's first lines.
/// It is bounded, so long text scrolls inside it and never pushes the conversation away, and it is one tab stop with
/// a name, so it scrolls by keyboard too. Error output is tagged with the name of its stream, and also colored. Shown
/// from its end, it keeps the newest line in view while output arrives. Where <see cref="CopyLabel"/> is set, the copy
/// icon stands in its top corner, beside the text and never over it, and copies all of the output as it was printed.
/// </summary>
internal sealed class OutputBox : Grid
{
    #region Private Fields

    private readonly Border box = new();

    private readonly TextBlock words = Styles.Text(null, "DvTextMono");

    private readonly ScrollViewer scroll;

    // Named from the start: an icon button without a name may not load, even while it is hidden.
    private readonly CopyButton copy = new() { Label = OutputWords.CopyOutput, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };

    private string? prompt;

    private string? lead;

    private IReadOnlyList<ToolOutputPiece> pieces = [];

    #endregion

    #region Constructors

    public OutputBox()
    {
        words.IsTextSelectionEnabled = true;
        scroll = new()
        {
            Content = words,
            Padding = Tokens.Space("DvSpace3", "DvSpace3"),
            MaxHeight = Tokens.Number("DvOutputLines") * (double)Styles.Dictionary["DvLine14"],
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsTabStop = true,
            UseSystemFocusVisuals = true,
            FocusVisualPrimaryThickness = new(Tokens.Number("DvFocusWidth")),
            FocusVisualSecondaryThickness = new(0),
            FocusVisualMargin = new(Tokens.Number("DvFocusWidth")),
        };
        copy.Text = () => OutputWords.CopyText(pieces);
        copy.Margin = new(0, Tokens.Number("DvSpace1"), Tokens.Number("DvSpace1"), 0);
        var inside = new Grid();
        inside.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        inside.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        SetColumn(copy, 1);
        inside.Children.Add(scroll);
        inside.Children.Add(copy);
        box.Child = inside;
        box.BorderThickness = new(Tokens.Number("DvBorder"));
        Children.Add(box);
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    /// <summary>What the box holds, for someone who cannot see it: "Output", "The first lines of notes.txt".</summary>
    public string Label
    {
        get => AutomationProperties.GetName(scroll);
        set => AutomationProperties.SetName(scroll, value);
    }

    /// <summary>The copy icon's name, "Copy output", or nothing for a box that offers no copy, such as a file's first lines.</summary>
    public string? CopyLabel { get; set; }

    /// <summary>The surface, as a token name: the card color inside a card, the app background on a panel.</summary>
    public string Fill { get; set; } = "DvBg2";

    /// <summary>True for a box with the card's rounded corners. A terminal's are square.</summary>
    public bool Rounded { get; set; } = true;

    /// <summary>True when the newest text matters most: the box stays at its end as text arrives.</summary>
    public bool FollowsEnd { get; set; }

    #endregion

    #region Public Methods

    /// <summary>Shows plain text: a file's lines.</summary>
    public void Show(string? text) => Show(null, text, []);

    /// <summary>Shows what a tool printed, piece by piece, after the command that printed it where there is one.</summary>
    public void Show(string? promptMark, string? commandLine, IReadOnlyList<ToolOutputPiece> output)
    {
        (prompt, lead, pieces) = (promptMark, commandLine, output);
        copy.Label = CopyLabel ?? OutputWords.CopyOutput;
        copy.Visibility = CopyLabel is not null && pieces.Any(piece => piece.Text.Length > 0) ? Visibility.Visible : Visibility.Collapsed;
        Paint();

        if (FollowsEnd)
        {
            scroll.UpdateLayout();
            scroll.ChangeView(null, scroll.ScrollableHeight, null, disableAnimation: true);
        }
    }

    /// <summary>Moves the focus here, for when this text takes the place of the control that brought it.</summary>
    public void TakeFocus() => scroll.Focus(FocusState.Keyboard);

    #endregion

    #region Private Methods

    private void Paint()
    {
        box.Background = Tokens.Brush(this, Fill);
        box.BorderBrush = Tokens.Brush(this, "DvLine");
        box.CornerRadius = new(Rounded ? Tokens.Number("DvRadiusCard") : 0);
        scroll.FocusVisualPrimaryBrush = Tokens.Brush(this, "DvEmber");
        words.Foreground = Tokens.Brush(this, "DvText");
        words.Inlines.Clear();

        if (prompt is not null)
        {
            // The prompt is decoration: the command after it is what is read.
            words.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = prompt, Foreground = Tokens.Brush(this, "DvText3") });
        }

        if (lead is not null)
        {
            words.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = lead });
        }

        foreach (var piece in pieces)
        {
            if (piece.Stream is OutputWords.ErrorStream)
            {
                var danger = Tokens.Brush(this, "DvDanger");
                words.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = $"{OutputWords.ErrorStream}: ", Foreground = danger, FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"] });
                words.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = piece.Text, Foreground = danger });
            }
            else
            {
                words.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = piece.Text });
            }
        }
    }

    #endregion
}
