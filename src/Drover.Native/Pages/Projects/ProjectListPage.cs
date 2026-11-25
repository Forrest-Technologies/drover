namespace Drover.Native.Pages;

/// <summary>The projects of the space: open one, or make one from a dialog when the role allows it.</summary>
public sealed class ProjectListPage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly ProjectListViewModel model = Make<ProjectListViewModel>();

    private readonly Navigator navigator = ((App)Application.Current).Services.GetRequiredService<Navigator>();

    private readonly ScreenHeader header = new() { Title = ProjectListViewModel.Heading };

    private readonly IconButton newProjectButton = new()
    {
        Icon = IconName.Plus,
        Label = ProjectListViewModel.EmptyAction,
    };

    private readonly TextBlock memberNoteText = Styles.Text(ProjectListViewModel.MemberNote, "DvTextSecondary");

    private readonly StackPanel listPanel = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly NamedGroup projects =
        new(ProjectListViewModel.Heading, "DvSpace3") { Role = AutomationControlType.List };

    private readonly Field newProjectName = new() { Label = "Name" };

    private readonly StatusNote newProjectNote = new();

    private readonly Button cancelCreateButton = new() { Content = "Cancel" };

    private readonly Button createButton = new() { Content = "Create project", Kind = ButtonKind.Primary };

    private readonly Dialog newProject;

    private bool showing;

    private bool updating;

    #endregion

    #region Constructors

    public ProjectListPage()
    {
        AutomationProperties.SetAutomationId(newProjectButton, "NewProject");
        newProjectButton.Click += (_, _) =>
        {
            model.StartCreating();
            Refresh();
        };
        header.Actions.Add(newProjectButton);

        AutomationProperties.SetAutomationId(newProjectName, "NewProjectName");
        newProjectName.RegisterPropertyChangedCallback(Field.ValueProperty, (_, _) =>
        {
            if (!showing)
            {
                model.UpdateDraftName(newProjectName.Value);
            }
        });
        newProjectName.Submitted += async (_, _) =>
        {
            if (!model.CanManage || model.Working is not null)
            {
                return;
            }

            var pending = model.Create();
            Refresh();
            await pending;
            Refresh();
        };

        cancelCreateButton.Click += (_, _) =>
        {
            model.CancelCreating();
            Refresh();
        };
        createButton.Click += async (_, _) =>
        {
            var pending = model.Create();
            Refresh();
            await pending;
            Refresh();
        };

        var body = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        body.Children.Add(newProjectName);
        body.Children.Add(newProjectNote);
        newProject = new Dialog
        {
            Title = "New project",
            Body = body,
            Actions = PageParts.Row(cancelCreateButton, createButton),
            InitialFocus = newProjectName,
        };
        newProject.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) => ProjectDialogChanged());

        layout.Body.Add(header);
        layout.Body.Add(memberNoteText);
        layout.Body.Add(listPanel);
        layout.Body.Add(newProject);

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

    private void ProjectDialogChanged()
    {
        if (updating || newProject.Open)
        {
            return;
        }

        if (model.Creating)
        {
            model.CancelCreating();
            Refresh();
        }
    }

    private void Refresh()
    {
        if (updating)
        {
            return;
        }

        updating = true;
        try
        {
            RefreshView();
        }
        finally
        {
            updating = false;
        }
    }

    private void RefreshView()
    {
        var ready = model.State is ProjectListState.Empty or ProjectListState.Listed;
        memberNoteText.Visibility = model.State is ProjectListState.Listed && !model.CanManage
            ? Visibility.Visible
            : Visibility.Collapsed;
        newProjectButton.Visibility = model.ShowsNewProject ? Visibility.Visible : Visibility.Collapsed;

        showing = true;
        try
        {
            if (newProjectName.Value != model.DraftName)
            {
                newProjectName.Value = model.DraftName;
            }
        }
        finally
        {
            showing = false;
        }

        cancelCreateButton.IsEnabled = model.Working is null;
        createButton.IsEnabled = model.Working is null;
        newProjectNote.Text = model.Working ?? model.Problem;
        newProjectNote.Kind = model.Working is null ? NoteKind.Danger : NoteKind.Info;
        Open(newProject, model.Creating);

        // The control that opened the dialog stays alive, so closing it returns the focus there.
        if (model.Creating)
        {
            return;
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
            empty.Action += (_, _) =>
            {
                model.StartCreating();
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

    private static void Open(Dialog dialog, bool open)
    {
        if (dialog.Open != open)
        {
            dialog.Open = open;
        }
    }

    #endregion
}
