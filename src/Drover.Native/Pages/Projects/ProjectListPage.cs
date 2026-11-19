namespace Drover.Native.Pages;

/// <summary>The projects of the space: open one, or make one when the signed-in role allows it.</summary>
public sealed class ProjectListPage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly ProjectListViewModel model = Make<ProjectListViewModel>();

    private Field draftName = new();

    #endregion

    #region Constructors

    public ProjectListPage()
    {
        Content = layout;
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => ProjectListViewModel.Title;

    #endregion

    #region Private Methods

    protected override async void Opened(RouteMatch<Type> route)
    {
        await model.Open();
        Refresh();
    }

    private void Refresh()
    {
        var body = layout.Body;
        body.Clear();
        body.Add(PageParts.Title(ProjectListViewModel.Heading));

        if (model.State is ProjectListState.Failed)
        {
            body.Add(Failed(ProjectListViewModel.LoadFailedTitle, model.LoadProblem, model.Load));
            return;
        }

        if (model.State is ProjectListState.Loading)
        {
            body.Add(PageParts.Waiting(ProjectListViewModel.Loading));
            return;
        }

        if (!model.CanManage)
        {
            body.Add(Styles.Text(ProjectListViewModel.MemberNote, "DvTextSecondary"));
        }

        ShowCreate(body);
        if (model.Problem is { } problem)
        {
            body.Add(Styles.Text(problem, "DvTextSecondary"));
        }

        if (model.Working is { } working)
        {
            body.Add(PageParts.Waiting(working));
        }

        if (model.State is ProjectListState.Empty)
        {
            body.Add(Empty());
            return;
        }

        foreach (var project in model.Projects ?? [])
        {
            body.Add(ProjectLink(project));
        }
    }

    private void ShowCreate(UIElementCollection body)
    {
        draftName = PageParts.Field(
            "Project name",
            string.Empty,
            Remember,
            hint: "Name the project, then make it.",
            disabled: !model.CanManage);
        AutomationProperties.SetName(draftName, "Project name");
        body.Add(draftName);
        if (model.State is ProjectListState.Listed || !model.CanManage)
        {
            body.Add(PageParts.Action(
                ProjectListViewModel.EmptyAction,
                () => model.Create(draftName.Value),
                Refresh,
                model.CanManage && model.Working is null,
                ButtonKind.Primary));
        }
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
                var pending = model.Create(draftName.Value);
                Refresh();
                await pending;
                Refresh();
            };
        }

        return empty;
    }

    private Link ProjectLink(ProjectSummary project)
    {
        var link = new Link
        {
            Text = project.Name,
            Address = Addresses.Project(project.Id),
            MinHeight = Tokens.Number("DvTargetTouch"),
        };
        AutomationProperties.SetName(link, project.Name);
        return link;
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

    private static void Remember(string value) => _ = value;

    #endregion
}
