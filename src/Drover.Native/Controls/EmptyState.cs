namespace Drover.Native.Controls;

/// <summary>
/// What a screen shows when there is nothing to show: why, in plain words, and the one thing to do next. Both are
/// required, so an empty screen always says what is happening.
/// </summary>
public sealed class EmptyState : Grid
{
    #region Private Fields

    private readonly Facet frame = new();

    private readonly StackPanel column = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly TextBlock title = Styles.Text(null, "DvTextH3");

    private readonly TextBlock cause = Styles.Text(null, "DvTextSecondary");

    private readonly Link link = new() { MinHeight = Targets.Minimum };

    private readonly Button button = new() { Kind = ButtonKind.Primary, HorizontalAlignment = HorizontalAlignment.Left };

    #endregion

    #region Constructors

    public EmptyState()
    {
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        column.Children.Add(title);
        column.Children.Add(cause);
        column.Children.Add(link);
        column.Children.Add(button);
        frame.Cut = Tokens.Number("DvCut");
        frame.Padding = new(Tokens.Number("DvSpace5"));
        frame.Children.Add(column);
        Children.Add(frame);
        button.Click += (_, _) => Action?.Invoke(this, EventArgs.Empty);
        Loaded += (_, _) => Require();
        Describe();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty TitleProperty = Dp.Register<EmptyState, string?>(nameof(Title), null, state => state.Describe());

    public static readonly DependencyProperty CauseProperty = Dp.Register<EmptyState, string>(nameof(Cause), string.Empty, state => state.Describe());

    public static readonly DependencyProperty ActionLabelProperty =
        Dp.Register<EmptyState, string>(nameof(ActionLabel), string.Empty, state => state.Describe());

    public static readonly DependencyProperty ActionHrefProperty = Dp.Register<EmptyState, string?>(nameof(ActionHref), null, state => state.Describe());

    public static readonly DependencyProperty ActionCommandProperty =
        Dp.Register<EmptyState, System.Windows.Input.ICommand?>(nameof(ActionCommand), null, state => state.Describe());

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Why there is nothing here, in plain words. Required.</summary>
    public string Cause
    {
        get => (string)GetValue(CauseProperty);
        set => SetValue(CauseProperty, value);
    }

    /// <summary>The one thing to do next, named as an action.</summary>
    public string ActionLabel
    {
        get => (string)GetValue(ActionLabelProperty);
        set => SetValue(ActionLabelProperty, value);
    }

    /// <summary>Set when the next step is somewhere else. The action is then a link.</summary>
    public string? ActionHref
    {
        get => (string?)GetValue(ActionHrefProperty);
        set => SetValue(ActionHrefProperty, value);
    }

    /// <summary>What the action does, when it is bound to a view model.</summary>
    public System.Windows.Input.ICommand? ActionCommand
    {
        get => (System.Windows.Input.ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    /// <summary>Raised when the action is taken, for a screen that handles it in its own code.</summary>
    public event EventHandler? Action;

    #endregion

    #region Private Methods

    private void Describe()
    {
        title.Text = Title ?? string.Empty;
        title.Visibility = Title is null ? Visibility.Collapsed : Visibility.Visible;
        cause.Text = Cause;
        link.Text = ActionLabel;
        link.Address = ActionHref;
        link.Visibility = ActionHref is null ? Visibility.Collapsed : Visibility.Visible;
        button.Content = ActionLabel;
        button.Command = ActionCommand;
        button.Visibility = ActionHref is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Require()
    {
        if (string.IsNullOrWhiteSpace(Cause) || string.IsNullOrWhiteSpace(ActionLabel))
        {
            throw new InvalidOperationException($"{nameof(EmptyState)} needs a {nameof(Cause)} and an {nameof(ActionLabel)}.");
        }

        if (ActionHref is null && ActionCommand is null && Action is null)
        {
            throw new InvalidOperationException(
                $"{nameof(EmptyState)} needs {nameof(Action)}, {nameof(ActionCommand)}, or {nameof(ActionHref)}: its action must do something.");
        }
    }

    #endregion
}
