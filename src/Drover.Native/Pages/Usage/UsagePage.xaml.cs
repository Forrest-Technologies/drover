namespace Drover.Native.Pages;

/// <summary>
/// Which bot is spending the most, to decide what to change. The period and the chosen bot are in the address, so a
/// link to the page shows the same thing. Every address is a new page, so where focus belongs after a choice is
/// remembered across the step: a bot's name moves it to the runs that open, and a period chosen with the arrow keys
/// keeps it in the period control.
/// </summary>
public sealed partial class UsagePage : Screen
{
    #region Private Fields

    private static UsageFocus comingFocus;

    #endregion

    #region Constructors

    public UsagePage()
    {
        InitializeComponent();
        Model.Navigating += focus => comingFocus = focus;
        Model.PropertyChanged += (_, args) => MoveFocus(args.PropertyName);
    }

    #endregion

    #region Properties

    public override string Title => UsageViewModel.Title;

    public UsageViewModel Model { get; } = Make<UsageViewModel>();

    #endregion

    #region Private Methods

    protected override async void Opened(RouteMatch<Type> route) =>
        await Model.Open(route.QueryValue("period"), Guid.TryParse(route.QueryValue("bot"), out var bot) ? bot : null);

    private void ChooseBot(Guid bot) => Model.ChooseBot(bot);

    // Only a bot the person just chose moves focus to its runs, once they are there. A period keeps focus in its control.
    private void MoveFocus(string? changed)
    {
        if (comingFocus is UsageFocus.Runs && changed is nameof(UsageViewModel.Runs) && Model.FocusRuns)
        {
            comingFocus = UsageFocus.None;
            RunsHeading.Focus(FocusState.Programmatic);
        }
        else if (comingFocus is UsageFocus.Period && changed is nameof(UsageViewModel.ShowCrew) && Model.ShowCrew)
        {
            comingFocus = UsageFocus.None;
            Selected(Period)?.TakeFocus();
        }
    }

    // The chosen choice of the period control: the one that is its tab stop.
    private static Pressable? Selected(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Pressable { IsTabStop: true } choice)
            {
                return choice;
            }

            if (Selected(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    #endregion
}
