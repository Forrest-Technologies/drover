namespace Drover.Native.Session;

/// <summary>
/// Notifications on this device: what is so, in a sentence, and the one action when there is one. A state whose
/// next step is in the system, the operating system, or the server's setup says so and offers no dead button.
/// </summary>
public sealed class PushControl : StackPanel
{
    #region Private Fields

    private const string Heading = "Notifications on this device";

    private readonly TextBlock title = Styles.Text(Heading);

    private readonly TextBlock sentence = Styles.Text(null, "DvTextSecondary");

    private readonly TextBlock contents = Styles.Text(PushWords.Contents);

    private readonly Button action = new();

    private readonly StatusNote problem = new() { Kind = NoteKind.Danger };

    #endregion

    #region Constructors

    public PushControl()
    {
        Spacing = Tokens.Number("DvSpace2");
        title.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightHeading"];
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level3);
        AutomationProperties.SetName(this, Heading);
        action.HorizontalAlignment = HorizontalAlignment.Left;
        action.Click += (_, _) => Toggle?.Invoke(this, EventArgs.Empty);
        contents.Loaded += (_, _) => contents.Foreground = Tokens.Brush(contents, "DvText3");
        contents.ActualThemeChanged += (_, _) => contents.Foreground = Tokens.Brush(contents, "DvText3");
        Children.Add(title);
        Children.Add(sentence);
        Children.Add(contents);
        Children.Add(action);
        Children.Add(problem);
        Show();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty StateProperty = Dp.Register<PushControl, PushState>(nameof(State), PushState.Off, control => control.Show());

    public static readonly DependencyProperty WorkingProperty = Dp.Register<PushControl, bool>(nameof(Working), false, control => control.Show());

    public static readonly DependencyProperty ProblemProperty = Dp.Register<PushControl, string?>(nameof(Problem), null, control => control.Show());

    public static readonly DependencyProperty TitleHiddenProperty = Dp.Register<PushControl, bool>(nameof(TitleHidden), false, control => control.Show());

    public PushState State
    {
        get => (PushState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>True while the device is being asked or the server is being told.</summary>
    public bool Working
    {
        get => (bool)GetValue(WorkingProperty);
        set => SetValue(WorkingProperty, value);
    }

    /// <summary>Why turning it on or off failed, as a sentence.</summary>
    public string? Problem
    {
        get => (string?)GetValue(ProblemProperty);
        set => SetValue(ProblemProperty, value);
    }

    /// <summary>True where a heading around it already says this, such as a dialog's title. The section keeps its name.</summary>
    public bool TitleHidden
    {
        get => (bool)GetValue(TitleHiddenProperty);
        set => SetValue(TitleHiddenProperty, value);
    }

    /// <summary>Raised when the person asks to turn notifications on or off.</summary>
    public event EventHandler? Toggle;

    #endregion

    #region Private Methods

    private void Show()
    {
        title.Visibility = TitleHidden ? Visibility.Collapsed : Visibility.Visible;
        sentence.Text = PushWords.Sentence(State);
        problem.Text = Problem;

        var label = PushWords.Action(State);
        contents.Visibility = action.Visibility = label is null ? Visibility.Collapsed : Visibility.Visible;
        action.Content = label ?? string.Empty;
        action.Kind = State is PushState.Off ? ButtonKind.Primary : ButtonKind.Quiet;
        action.IsEnabled = !Working;
    }

    #endregion
}
