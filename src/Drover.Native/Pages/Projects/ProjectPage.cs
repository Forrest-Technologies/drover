namespace Drover.Native.Pages;

/// <summary>
/// One project. An owner or admin edits its name, instructions, and files. Anyone in the space reads them,
/// starts a conversation with a bot, and moves a conversation they own.
/// </summary>
public sealed class ProjectPage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly ProjectViewModel model = Make<ProjectViewModel>();

    private Field nameField = new();

    private TextArea instructionsField = new();

    #endregion

    #region Constructors

    public ProjectPage()
    {
        Content = layout;
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => ProjectViewModel.Title;

    #endregion

    #region Private Methods

    protected override async void Opened(RouteMatch<Type> route)
    {
        if (route.GuidParameter("ProjectId") is not { } projectId)
        {
            model.Absent();
            Refresh();
            return;
        }

        await model.Open(projectId);
        Refresh();
    }

    private void Refresh()
    {
        var body = layout.Body;
        body.Clear();
        body.Add(Back());

        if (model.State is ProjectScreenState.Loading)
        {
            body.Add(PageParts.Title("Project"));
            body.Add(PageParts.Waiting(ProjectViewModel.Loading));
            return;
        }

        if (model.State is ProjectScreenState.Missing)
        {
            body.Add(Missing());
            return;
        }

        if (model.State is ProjectScreenState.Failed)
        {
            body.Add(Failed(ProjectViewModel.LoadFailedTitle, model.LoadProblem, model.Load));
            return;
        }

        body.Add(PageParts.Title(model.Name));
        if (!model.CanManage)
        {
            body.Add(Styles.Text(ProjectViewModel.MemberNote, "DvTextSecondary"));
        }

        ShowEditor(body);
        if (model.Problem is { } problem)
        {
            body.Add(Styles.Text(problem, "DvTextSecondary"));
        }

        if (model.Working is { } working)
        {
            body.Add(PageParts.Waiting(working));
        }

        ShowFiles(body);
        ShowConversations(body);
        ShowStart(body);
        ShowMoveIn(body);
        ShowRemove(body);
    }

    private void ShowEditor(UIElementCollection body)
    {
        nameField = PageParts.Field("Name", model.Name, Remember, disabled: !model.CanManage);
        AutomationProperties.SetName(nameField, "Name");
        instructionsField = new TextArea
        {
            Label = "Instructions",
            Value = model.Instructions,
            Hint = "These belong to the project. They do not add a tool or change what a bot is allowed to do.",
            Disabled = !model.CanManage,
        };
        AutomationProperties.SetName(instructionsField, "Instructions");
        body.Add(nameField);
        body.Add(instructionsField);
        body.Add(PageParts.Action(
            "Save project",
            () => model.Save(nameField.Value, instructionsField.Value),
            Refresh,
            model.CanManage && model.Working is null,
            ButtonKind.Primary));
    }

    private void ShowFiles(UIElementCollection body)
    {
        body.Add(PageParts.CardTitle("Files"));
        if (model.Files.Count == 0)
        {
            body.Add(Styles.Text(ProjectViewModel.NoFiles, "DvTextSecondary"));
        }

        foreach (var file in model.Files)
        {
            var download = PageParts.Action(
                ProjectViewModel.DownloadLabel(file),
                () => OpenDownload(model.DownloadAddress(file.File.Id)),
                Refresh,
                model.Working is null);
            var label = Styles.Text(ProjectViewModel.FileLabel(file));
            if (model.CanManage)
            {
                var remove = PageParts.Action(
                    ProjectViewModel.RemoveFileLabel(file),
                    () => model.RemoveFile(file.File.Id),
                    Refresh,
                    model.Working is null,
                    ButtonKind.Danger);
                body.Add(PageParts.Row(label, download, remove));
            }
            else
            {
                body.Add(PageParts.Row(label, download));
            }
        }

        body.Add(PageParts.Action(ProjectViewModel.AddFile, AddFile, Refresh, model.CanManage && model.Working is null));
    }

    private void ShowConversations(UIElementCollection body)
    {
        body.Add(PageParts.CardTitle("Conversations"));
        if (model.Threads.Count == 0)
        {
            body.Add(Styles.Text(ProjectViewModel.NoConversations, "DvTextSecondary"));
        }

        foreach (var thread in model.Threads)
        {
            var label = model.ThreadLabel(thread);
            body.Add(PageParts.Row(ThreadLink(label, Addresses.Conversation(thread.Id)), MoveOut(label, thread.Id)));
        }
    }

    private void ShowStart(UIElementCollection body)
    {
        body.Add(PageParts.CardTitle("Start a conversation"));
        if (model.ActiveBots.Count == 0)
        {
            body.Add(Styles.Text(ProjectViewModel.NoBot, "DvTextSecondary"));
            return;
        }

        foreach (var bot in model.ActiveBots)
        {
            var label = ProjectViewModel.StartLabel(bot);
            body.Add(PageParts.Action(label, () => model.Start(bot.Id), Refresh, model.Working is null));
        }
    }

    private void ShowMoveIn(UIElementCollection body)
    {
        body.Add(PageParts.CardTitle("Move a conversation in"));
        if (model.Movable.Count == 0)
        {
            body.Add(Styles.Text(ProjectViewModel.NoMovable, "DvTextSecondary"));
            return;
        }

        foreach (var thread in model.Movable)
        {
            var label = ProjectViewModel.MoveInLabel(model.ThreadLabel(thread));
            body.Add(PageParts.Action(label, () => model.MoveIn(thread.Id), Refresh, model.Working is null));
        }
    }

    private void ShowRemove(UIElementCollection body)
    {
        body.Add(PageParts.CardTitle("Remove"));
        if (model.ConfirmingRemove)
        {
            body.Add(Styles.Text(ProjectViewModel.RemoveQuestion, "DvText"));
            body.Add(PageParts.Row(
                PageParts.Action(ProjectViewModel.RemoveProject, model.Remove, Refresh, model.CanManage && model.Working is null, ButtonKind.Danger),
                PageParts.Action(ProjectViewModel.KeepProject, Keep, Refresh, model.Working is null)));
            return;
        }

        body.Add(PageParts.Action(
            ProjectViewModel.RemoveProject,
            AskRemove,
            Refresh,
            model.CanManage && model.Working is null,
            ButtonKind.Danger));
    }

    private Button MoveOut(string label, Guid threadId) =>
        PageParts.Action(ProjectViewModel.MoveOutLabel(label), () => model.MoveOut(threadId), Refresh, model.Working is null);

    private static Link Back()
    {
        var link = new Link
        {
            Text = ProjectViewModel.MissingAction,
            Address = Addresses.Projects,
            MinHeight = Tokens.Number("DvTargetTouch"),
        };
        AutomationProperties.SetName(link, ProjectViewModel.MissingAction);
        return link;
    }

    private static Link ThreadLink(string label, string address)
    {
        var link = new Link
        {
            Text = label,
            Address = address,
            MinHeight = Tokens.Number("DvTargetTouch"),
        };
        AutomationProperties.SetName(link, label);
        return link;
    }

    private EmptyState Missing()
    {
        return new EmptyState
        {
            Title = ProjectViewModel.MissingTitle,
            Cause = ProjectViewModel.MissingCause,
            ActionLabel = ProjectViewModel.MissingAction,
            ActionHref = Addresses.Projects,
        };
    }

    private EmptyState Failed(string title, string? cause, Func<Task> retry)
    {
        var failed = new EmptyState
        {
            Title = title,
            Cause = string.IsNullOrWhiteSpace(cause) ? title : cause,
            ActionLabel = ProjectViewModel.TryAgain,
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

    private async Task AddFile()
    {
        foreach (var file in await PickedFiles.Choose())
        {
            await model.Upload(file);
        }
    }

    private Task AskRemove()
    {
        model.PrepareRemove();
        return Task.CompletedTask;
    }

    private Task Keep()
    {
        model.CancelRemove();
        return Task.CompletedTask;
    }

    private static Task OpenDownload(string address)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            _ = Launcher.LaunchUriAsync(uri);
        }

        return Task.CompletedTask;
    }

    private static void Remember(string value) => _ = value;

    #endregion
}
