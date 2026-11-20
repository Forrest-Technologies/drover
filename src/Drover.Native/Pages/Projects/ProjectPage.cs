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

    private readonly StackPanel statePanel = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly StackPanel editorPanel = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly StackPanel statusPanel = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly StackPanel detailsPanel = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly TextBlock titleText = PageParts.Title("Project");

    private readonly TextBlock memberNoteText = Styles.Text(ProjectViewModel.MemberNote, "DvTextSecondary");

    private readonly Field nameField;

    private readonly TextArea instructionsField;

    private readonly Button saveButton;

    private bool showing;

    #endregion

    #region Constructors

    public ProjectPage()
    {
        nameField = PageParts.Field(
            "Name",
            string.Empty,
            value =>
            {
                if (!showing)
                {
                    model.UpdateDraftName(value);
                }
            });
        AutomationProperties.SetName(nameField, "Name");

        instructionsField = new TextArea
        {
            Label = "Instructions",
            Hint = "These belong to the project. They do not add a tool or change what a bot is allowed to do.",
        };
        AutomationProperties.SetName(instructionsField, "Instructions");
        instructionsField.RegisterPropertyChangedCallback(
            TextArea.ValueProperty,
            (_, _) =>
            {
                if (!showing)
                {
                    model.UpdateDraftInstructions(instructionsField.Value);
                }
            });

        saveButton = PageParts.Action(
            "Save project",
            () => model.Save(),
            Refresh,
            enabled: false,
            ButtonKind.Primary);

        editorPanel.Children.Add(titleText);
        editorPanel.Children.Add(memberNoteText);
        editorPanel.Children.Add(nameField);
        editorPanel.Children.Add(instructionsField);
        editorPanel.Children.Add(saveButton);

        layout.Body.Add(Back());
        layout.Body.Add(statePanel);
        layout.Body.Add(editorPanel);
        layout.Body.Add(statusPanel);
        layout.Body.Add(detailsPanel);

        Unloaded += (_, _) => model.Cancel();
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
        var ready = model.State is ProjectScreenState.Ready;
        statePanel.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        editorPanel.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        statusPanel.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        detailsPanel.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;

        statePanel.Children.Clear();
        if (!ready)
        {
            if (model.State is ProjectScreenState.Loading)
            {
                statePanel.Children.Add(PageParts.Title("Project"));
                statePanel.Children.Add(PageParts.Waiting(ProjectViewModel.Loading));
            }
            else if (model.State is ProjectScreenState.Missing)
            {
                statePanel.Children.Add(Missing());
            }
            else
            {
                statePanel.Children.Add(Failed(ProjectViewModel.LoadFailedTitle, model.LoadProblem, model.Load));
            }

            statusPanel.Children.Clear();
            detailsPanel.Children.Clear();
            return;
        }

        showing = true;
        try
        {
            titleText.Text = model.Name;
            AutomationProperties.SetName(titleText, model.Name);
            memberNoteText.Visibility = model.CanManage ? Visibility.Collapsed : Visibility.Visible;

            if (nameField.Value != model.DraftName)
            {
                nameField.Value = model.DraftName;
            }

            nameField.Disabled = !model.CanManage;

            if (instructionsField.Value != model.DraftInstructions)
            {
                instructionsField.Value = model.DraftInstructions;
            }

            instructionsField.Disabled = !model.CanManage;
            saveButton.IsEnabled = model.CanManage && model.Working is null;
        }
        finally
        {
            showing = false;
        }

        statusPanel.Children.Clear();
        if (model.Problem is { } problem)
        {
            statusPanel.Children.Add(Styles.Text(problem, "DvTextSecondary"));
        }

        if (model.Working is { } working)
        {
            statusPanel.Children.Add(PageParts.Waiting(working));
        }

        var details = detailsPanel.Children;
        details.Clear();
        ShowFiles(details);
        ShowConversations(details);
        ShowStart(details);
        ShowMoveIn(details);
        ShowRemove(details);
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
            Func<Task> downloadAction = model.Delivers
                ? () => model.Download(file)
                : () => OpenDownload(model.DownloadAddress(file.File.Id));
            var download = PageParts.Action(
                ProjectViewModel.DownloadLabel(file),
                downloadAction,
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
            var link = ThreadLink(label, Addresses.Conversation(thread.Id));
            body.Add(model.CanMoveOut(thread.Id)
                ? PageParts.Row(link, MoveOut(label, thread.Id))
                : PageParts.Row(link));
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

    private static EmptyState Missing() => new()
    {
        Title = ProjectViewModel.MissingTitle,
        Cause = ProjectViewModel.MissingCause,
        ActionLabel = ProjectViewModel.MissingAction,
        ActionHref = Addresses.Projects,
    };

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

    private Task AddFile() => model.ChooseFiles(PickedFiles.Choose);

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

    #endregion
}
