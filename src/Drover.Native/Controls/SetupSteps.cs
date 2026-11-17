namespace Drover.Native.Controls;

/// <summary>Where a person is in first-run setup: a count in words, and a bar for each step that says the same.</summary>
public sealed class SetupSteps : StackPanel
{
    #region Private Fields

    private static readonly string[] names = ["Create your account", "Connect a model", "Meet your first bot"];

    private readonly TextBlock count = Styles.Text(null, "DvTextCaption");

    private readonly Grid bars = new() { ColumnSpacing = Tokens.Number("DvSpace1") };

    #endregion

    #region Constructors

    public SetupSteps()
    {
        Spacing = Tokens.Number("DvSpace2");
        count.FontSize = Tokens.Number("DvText13");
        AutomationProperties.SetName(this, "Setup steps");
        AutomationProperties.SetLandmarkType(this, AutomationLandmarkType.Navigation);

        foreach (var _ in names)
        {
            bars.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var bar = new Border { Height = Tokens.Number("DvFocusWidth"), CornerRadius = new(Tokens.Number("DvRadiusFull")) };
            Grid.SetColumn(bar, bars.Children.Count);
            bars.Children.Add(bar);
        }

        Children.Add(count);
        Children.Add(bars);
        this.WhenShownOrRethemed(Show);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty CurrentProperty = Dp.Register<SetupSteps, int>(nameof(Current), 1, steps => steps.Show());

    /// <summary>The step on screen, counted from one.</summary>
    public int Current
    {
        get => (int)GetValue(CurrentProperty);
        set => SetValue(CurrentProperty, value);
    }

    #endregion

    #region Private Methods

    private void Show()
    {
        count.Text = $"Step {Current} of {names.Length}";

        for (var number = 1; number <= names.Length; number++)
        {
            var bar = (Border)bars.Children[number - 1];
            bar.Background = Tokens.Brush(this, number <= Current ? "DvText2" : "DvLine");
            AutomationProperties.SetName(bar, $"{names[number - 1]}{StateWords(number)}");
        }
    }

    private string StateWords(int number) => number < Current ? ", done" : number == Current ? ", this step" : ", not started";

    #endregion
}
