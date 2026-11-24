namespace Drover.Native.Pages;

/// <summary>The projects of the space: open one, or make one when the signed-in role allows it.</summary>
public sealed class ProjectListPage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly ProjectListViewModel model = Make<ProjectListViewModel>();

    private readonly Navigator navigator = ((App)Application.Current).Services.GetRequiredService<Navigator>();

    private readonly TextBlock titleText = PageParts.Title(ProjectListViewModel.Heading);

    private readonly TextBlock memberNoteText = Styles.Text(ProjectListViewModel.MemberNote, "DvTextSecondary");

    private readonly StackPanel createPanel = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly StackPanel statusPanel = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly StackPanel listPanel = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly NamedGroup projects = new(ProjectListViewModel.Heading, "DvSpace3") { Role = AutomationControlType.List };

    private readonly Field draftName;

    private readonly Button createButton;

    private bool showing;

    #endregion

    #region Constructors

    public ProjectListPage()
    {
        draftName = PageParts.Field(
            "Project name",
            string.Empty,
            value =>
            {
                if (!showing)
                {
                    model.UpdateDraftName(value);
                }
            },
            hint: "Name the project, then make it.");
        AutomationProperties.SetName(draftName, "Project name");
        createButton = PageParts.Action(
            ProjectListViewModel.EmptyAction,
            () => model.Create(),
            Refresh,
            enabled: false,
            ButtonKind.Primary);
        draftName.Submitted += async (_, _) =>
        {
            if (model.CanManage && model.Working is null)
            {
                var pending = model.Create();
                Refresh();
                await pending;
                Refresh();
            }
        };

        createPanel.Children.Add(draftName);
        createPanel.Children.Add(createButton);

        layout.Body.Add(titleText);
        layout.Body.Add(memberNoteText);
        layout.Body.Add(createPanel);
        layout.Body.Add(statusPanel);
        layout.Body.Add(listPanel);

        Unloaded += (_, _) => model.Cancel();
        Content = layout;
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => ProjectListViewModel.Title;

    #endregion

    #region Private Methods

    protected override async Task Opened(RouteMatch<Type> route)
    {
        await model.Open();
        Refresh();
    }

    private void Refresh()
    {
        var ready = model.State is ProjectListState.Empty or ProjectListState.Listed;
        memberNoteText.Visibility = ready && !model.CanManage ? Visibility.Visible : Visibility.Collapsed;
        createPanel.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;

        showing = true;
        try
        {
            if (draftName.Value != model.DraftName)
            {
                draftName.Value = model.DraftName;
            }

            draftName.Disabled = !model.CanManage;
            createButton.IsEnabled = model.CanManage && model.Working is null;
            createButton.Visibility = (model.State is ProjectListState.Listed || !model.CanManage)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        finally
        {
            showing = false;
        }

        statusPanel.Children.Clear();
        if (ready && model.Problem is { } problem)
        {
            statusPanel.Children.Add(Styles.Text(problem, "DvTextSecondary"));
        }

        if (ready && model.Working is { } working)
        {
            statusPanel.Children.Add(PageParts.Waiting(working));
        }

        listPanel.Children.Clear();

        if (model.State is ProjectListState.Failed)
        {
            listPanel.Children.Add(Failed(ProjectListViewModel.LoadFailedTitle, model.LoadProblem, model.Load));
            return;
        }

        if (model.State is ProjectListState.Loading)
        {
            listPanel.Children.Add(PageParts.Waiting(ProjectListViewModel.Loading));
            return;
        }

        if (model.State is ProjectListState.Empty)
        {
            listPanel.Children.Add(Empty());
            return;
        }

        projects.Children.Clear();
        foreach (var project in model.Projects ?? [])
        {
            projects.Children.Add(ProjectCard(project));
        }

        listPanel.Children.Add(projects);
    }

    private EmptyState Empty()
    {
        var empty = new EmptyState
        {
            Title = ProjectListViewModel.EmptyTitle,
            Cause = model.CanManage ? ProjectListViewModel.EmptyCause : ProjectListViewModel.MemberEmptyCause,
            ActionLabel = model.CanManage ? ProjectListViewModel.EmptyAction : ProjectListViewModel.MemberEmptyAction,
            ActionHref = model.CanManage ? null : Addresses.Home,
        };
        if (model.CanManage)
        {
            empty.Action += async (_, _) =>
            {
                var pending = model.Create();
                Refresh();
                await pending;
                Refresh();
            };
        }

        return empty;
    }

    private ListCard ProjectCard(ProjectSummary project)
    {
        var icon = new Icon { NameOfIcon = IconName.Project, Size = 20, VerticalAlignment = VerticalAlignment.Center };
        icon.WhenShownOrRethemed(() => icon.Foreground = Tokens.Brush(icon, "DvText2"));
        var card = new ListCard
        {
            LinkAutomationId = "ProjectCard",
            Leading = icon,
            Title = project.Name,
            Facts = ProjectListViewModel.Facts(project),
        };
        card.Opened += () => navigator.NavigateTo(Addresses.Project(project.Id));
        return card;
    }

    private EmptyState Failed(string title, string? cause, Func<Task> retry)
    {
        var failed = new EmptyState
        {
            Title = title,
            Cause = string.IsNullOrWhiteSpace(cause) ? title : cause,
            ActionLabel = ProjectListViewModel.TryAgain,
        };
        failed.Action += async (_, _) =>
        {
            var pending = retry();
            Refresh();
            await pending;
            Refresh();
        };
        return failed;
    }

    #endregion
}
