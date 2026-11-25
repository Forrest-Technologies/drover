namespace Drover.Native.Pages;

/// <summary>
/// One project, read first: what it holds, its conversations and its files. An owner or admin opens the sheet to
/// change its name and instructions, and asks before anything is removed.
/// </summary>
public sealed class ProjectPage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly ProjectViewModel model = Make<ProjectViewModel>();

    private readonly Navigator navigator = ((App)Application.Current).Services.GetRequiredService<Navigator>();

    private readonly StackPanel statePanel = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly StatusNote statusNote = new();

    private readonly StackPanel bodyPanel = new() { Spacing = Tokens.Number("DvSpace5") };

    private readonly ScreenHeader header = new()
    {
        Title = "Project",
        BackLabel = "Back to projects",
        BackAddress = Addresses.Projects,
    };

    private readonly IconButton editButton = new() { Icon = IconName.Edit, Label = "Edit project" };

    private readonly Menu moreMenu = new()
    {
        Label = "More",
        TriggerIcon = IconName.More,
        ShowTriggerLabel = false,
    };

    private readonly Field projectNameField = new() { Label = "Name" };

    private readonly TextArea instructionsField = new()
    {
        Label = "Instructions",
        Rows = 8,
        Hint = ProjectViewModel.InstructionsHint,
    };

    private readonly StatusNote editNote = new();

    private readonly TextBlock discardBody = Styles.Text(null, "DvTextParagraph");

    private readonly StatusNote removeProblem = new() { Kind = NoteKind.Danger };

    private readonly Button cancelEditButton = new() { Content = "Cancel" };

    private readonly Button saveButton = new() { Content = "Save changes", Kind = ButtonKind.Primary };

    private readonly Button keepEditingButton = new() { Content = ProjectViewModel.KeepEditingLabel };

    private readonly Button discardButton = new() { Content = ProjectViewModel.DiscardLabel, Kind = ButtonKind.Danger };

    private readonly Button doneButton = new() { Content = ProjectViewModel.Done };

    private readonly Button keepProjectButton = new() { Content = ProjectViewModel.KeepProject };

    private readonly Button removeProjectButton = new()
    {
        Content = ProjectViewModel.RemoveProject,
        Kind = ButtonKind.Danger,
    };

    private readonly Button keepFileButton = new() { Content = ProjectViewModel.KeepFileLabel };

    private readonly Button removeFileButton = new()
    {
        Content = ProjectViewModel.RemoveFileAction,
        Kind = ButtonKind.Danger,
    };

    private readonly StackPanel movableRows = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly Sheet editSheet;

    private readonly Dialog discardDialog;

    private readonly Dialog moveDialog;

    private readonly Dialog removeDialog;

    private readonly Dialog removeFileDialog;

    private bool showing;

    private bool updating;

    #endregion

    #region Constructors

    public ProjectPage()
    {
        AutomationProperties.SetAutomationId(editButton, "EditProject");
        editButton.Click += (_, _) =>
        {
            model.StartEditing();
            Refresh();
        };
        moreMenu.Items.Add(PageParts.MenuEntry(ProjectViewModel.RemoveProject, () =>
        {
            model.PrepareRemove();
            Refresh();
        }));
        header.Actions.Add(editButton);
        header.Actions.Add(moreMenu);

        AutomationProperties.SetAutomationId(projectNameField, "ProjectNameField");
        projectNameField.RegisterPropertyChangedCallback(Field.ValueProperty, (_, _) =>
        {
            if (!showing)
            {
                model.UpdateDraftName(projectNameField.Value);
            }
        });
        projectNameField.Submitted += async (_, _) =>
        {
            if (model.CanManage && model.Working is null)
            {
                await SaveFromSheet();
            }
        };

        AutomationProperties.SetAutomationId(instructionsField, "ProjectInstructionsField");
        instructionsField.RegisterPropertyChangedCallback(TextArea.ValueProperty, (_, _) =>
        {
            if (!showing)
            {
                model.UpdateDraftInstructions(instructionsField.Value);
            }
        });

        cancelEditButton.Click += (_, _) =>
        {
            model.CancelEditing();
            Refresh();
        };
        saveButton.Click += async (_, _) => await SaveFromSheet();
        keepEditingButton.Click += (_, _) =>
        {
            model.KeepEditing();
            Refresh();
        };
        discardButton.Click += (_, _) => DiscardEdits();
        doneButton.Click += (_, _) =>
        {
            model.StopMoving();
            Refresh();
        };
        keepProjectButton.Click += (_, _) =>
        {
            model.CancelRemove();
            Refresh();
        };
        removeProjectButton.Click += async (_, _) => await Run(model.Remove);
        keepFileButton.Click += (_, _) =>
        {
            model.KeepFile();
            Refresh();
        };
        removeFileButton.Click += async (_, _) =>
        {
            if (model.RemovingFile is { } file)
            {
                var pending = model.RemoveFile(file);
                model.KeepFile();
                Refresh();
                await pending;
                Refresh();
            }
        };

        editSheet = new Sheet
        {
            Title = "Edit project",
            Body = PageParts.SheetBody(projectNameField, instructionsField, editNote),
            Actions = PageParts.Row(cancelEditButton, saveButton),
            InitialFocus = projectNameField,
            Closing = AskBeforeDiscard,
        };
        AutomationProperties.SetAutomationId(editSheet, "EditProjectSheet");

        discardDialog = new Dialog
        {
            Title = "Discard changes?",
            Body = discardBody,
            Actions = PageParts.Row(keepEditingButton, discardButton),
            InitialFocus = keepEditingButton,
        };
        moveDialog = new Dialog
        {
            Title = ProjectViewModel.MoveHere,
            Body = movableRows,
            Actions = doneButton,
            InitialFocus = doneButton,
        };
        var removeBody = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        removeBody.Children.Add(Styles.Text(ProjectViewModel.RemoveQuestion, "DvTextParagraph"));
        removeBody.Children.Add(removeProblem);
        removeDialog = new Dialog
        {
            Body = removeBody,
            Actions = PageParts.Row(keepProjectButton, removeProjectButton),
            InitialFocus = keepProjectButton,
        };
        removeFileDialog = new Dialog
        {
            Body = Styles.Text(ProjectViewModel.RemoveFileQuestion, "DvTextParagraph"),
            Actions = PageParts.Row(keepFileButton, removeFileButton),
            InitialFocus = keepFileButton,
        };

        Watch(editSheet, () =>
        {
            if (model.Editing)
            {
                model.CancelEditing();
            }
        });
        Watch(discardDialog, () =>
        {
            if (model.ConfirmingDiscard)
            {
                model.KeepEditing();
            }
        });
        Watch(moveDialog, model.StopMoving);
        Watch(removeDialog, model.CancelRemove);
        Watch(removeFileDialog, model.KeepFile);

        layout.Body.Add(header);
        layout.Body.Add(statusNote);
        layout.Body.Add(statePanel);
        layout.Body.Add(bodyPanel);
        layout.Body.Add(editSheet);
        layout.Body.Add(discardDialog);
        layout.Body.Add(moveDialog);
        layout.Body.Add(removeDialog);
        layout.Body.Add(removeFileDialog);

        Unloaded += (_, _) => model.Cancel();
        Content = layout;
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => ProjectViewModel.Title;

    #endregion

    #region Private Methods

    protected override async Task Opened(RouteMatch<Type> route)
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
        var ready = model.State is ProjectScreenState.Ready;
        header.Title = ready ? model.Name : "Project";
        editButton.Visibility = ready && model.CanManage ? Visibility.Visible : Visibility.Collapsed;
        moreMenu.Visibility = ready && model.CanManage ? Visibility.Visible : Visibility.Collapsed;
        statePanel.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        statusNote.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        bodyPanel.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;

        statusNote.Text = model.Editing ? null : model.Working ?? model.Problem;
        statusNote.Kind = model.Working is null ? NoteKind.Danger : NoteKind.Info;

        statePanel.Children.Clear();
        if (!ready)
        {
            if (model.State is ProjectScreenState.Loading)
            {
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

            ShowDialogs();
            return;
        }

        showing = true;
        try
        {
            if (projectNameField.Value != model.DraftName)
            {
                projectNameField.Value = model.DraftName;
            }

            if (instructionsField.Value != model.DraftInstructions)
            {
                instructionsField.Value = model.DraftInstructions;
            }
        }
        finally
        {
            showing = false;
        }

        var idle = model.Working is null;
        saveButton.IsEnabled = model.CanManage && idle;
        cancelEditButton.IsEnabled = idle;
        keepEditingButton.IsEnabled = idle;
        discardButton.IsEnabled = idle;
        doneButton.IsEnabled = idle;
        keepProjectButton.IsEnabled = idle;
        removeProjectButton.IsEnabled = model.CanManage && idle;
        keepFileButton.IsEnabled = idle;
        removeFileButton.IsEnabled = model.CanManage && idle;
        editNote.Text = model.Working ?? model.Problem;
        editNote.Kind = model.Working is null ? NoteKind.Danger : NoteKind.Info;
        discardBody.Text = ProjectViewModel.DiscardQuestion(model.Name);
        removeProblem.Text = model.Problem;
        removeDialog.Title = ProjectViewModel.RemoveTitle(model.Name);
        removeFileDialog.Title = model.RemovingFile is { } file
            ? ProjectViewModel.RemoveTitle(file.File.Name)
            : string.Empty;

        ShowMovable();
        ShowDialogs();

        // Keep the control that opened a dialog alive, so closing it can return the focus there.
        if (model.Editing || model.ConfirmingDiscard || model.MovingConversations
            || model.ConfirmingRemove || model.RemovingFile is not null)
        {
            return;
        }

        bodyPanel.Children.Clear();
        ShowInstructions();
        ShowConversations();
        ShowFiles();
    }

    private void ShowDialogs()
    {
        Open(editSheet, model.Editing);
        Open(discardDialog, model.ConfirmingDiscard);
        Open(moveDialog, model.MovingConversations);
        Open(removeDialog, model.ConfirmingRemove);
        Open(removeFileDialog, model.RemovingFile is not null);
    }

    private void ShowInstructions()
    {
        bodyPanel.Children.Add(PageParts.CardTitle("Instructions"));
        var instructions = model.Instructions;
        bodyPanel.Children.Add(string.IsNullOrWhiteSpace(instructions)
            ? Styles.Text(ProjectViewModel.NoInstructions, "DvTextSecondary")
            : Selectable(instructions));
    }

    private void ShowConversations()
    {
        var add = new Menu { Label = "New conversation", TriggerIcon = IconName.Plus, ShowTriggerLabel = false };
        foreach (var bot in model.ActiveBots)
        {
            add.Items.Add(TaskItem(bot.Name, () => model.Start(bot.Id)));
        }

        add.Items.Add(PageParts.MenuEntry(ProjectViewModel.MoveHere, () =>
        {
            model.StartMoving();
            Refresh();
        }));
        bodyPanel.Children.Add(Heading("Conversations", add));

        if (model.ActiveBots.Count == 0)
        {
            bodyPanel.Children.Add(Styles.Text(ProjectViewModel.NoBot, "DvTextSecondary"));
        }

        if (model.Threads.Count == 0)
        {
            bodyPanel.Children.Add(Styles.Text(ProjectViewModel.NoConversations, "DvTextSecondary"));
            return;
        }

        foreach (var thread in model.Threads)
        {
            bodyPanel.Children.Add(ConversationCard(thread));
        }
    }

    private void ShowFiles()
    {
        IconButton? add = null;
        if (model.CanManage)
        {
            add = new IconButton
            {
                Icon = IconName.Attach,
                Label = ProjectViewModel.AddFile,
                IsEnabled = model.Working is null,
            };
            add.Click += async (_, _) => await Run(AddFile);
        }

        bodyPanel.Children.Add(Heading("Files", add));
        if (model.Files.Count == 0)
        {
            bodyPanel.Children.Add(Styles.Text(ProjectViewModel.NoFiles, "DvTextSecondary"));
            return;
        }

        foreach (var file in model.Files)
        {
            bodyPanel.Children.Add(FileRow(file));
        }
    }

    private void ShowMovable()
    {
        movableRows.Children.Clear();
        if (model.Movable.Count == 0)
        {
            movableRows.Children.Add(Styles.Text(ProjectViewModel.NoMovable, "DvTextSecondary"));
            return;
        }

        foreach (var thread in model.Movable)
        {
            var label = model.ThreadLabel(thread);
            var move = new Button { Content = "Move", IsEnabled = model.Working is null };
            AutomationProperties.SetName(move, ProjectViewModel.MoveToProjectLabel(label));
            move.Click += async (_, _) => await Run(() => model.MoveIn(thread.Id));
            var row = PageParts.Row(Styles.Text(label));
            row.Children.Add(move);
            movableRows.Children.Add(row);
        }
    }

    private Grid FileRow(ProjectFileResponse file)
    {
        var canDownload = model.Delivers || OperatingSystem.IsBrowser();
        var row = new Grid
        {
            ColumnSpacing = Tokens.Number("DvSpace2"),
            MinHeight = Tokens.Number("DvTargetTouch"),
        };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        if (canDownload)
        {
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        }

        if (model.CanManage)
        {
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        }

        var icon = new Icon { NameOfIcon = IconName.File, VerticalAlignment = VerticalAlignment.Center };
        var name = Styles.Text(file.File.Name);
        name.TextWrapping = TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.VerticalAlignment = VerticalAlignment.Center;
        var size = Styles.Text(Words.Size(file.File.Size), "DvTextMono");
        size.Foreground = Tokens.Brush(size, "DvText2");
        size.VerticalAlignment = VerticalAlignment.Center;
        var column = 0;
        Grid.SetColumn(icon, column++);
        Grid.SetColumn(name, column++);
        Grid.SetColumn(size, column++);
        row.Children.Add(icon);
        row.Children.Add(name);
        row.Children.Add(size);

        if (canDownload)
        {
            var download = new IconButton
            {
                Icon = IconName.Download,
                Label = ProjectViewModel.DownloadLabel(file),
                IsEnabled = model.Working is null,
            };
            download.Click += async (_, _) =>
            {
                Func<Task> work = model.Delivers
                    ? () => model.Download(file)
                    : () => OpenDownload(model.DownloadAddress(file.File.Id));
                await Run(work);
            };
            Grid.SetColumn(download, column++);
            row.Children.Add(download);
        }

        if (model.CanManage)
        {
            var remove = new IconButton
            {
                Icon = IconName.Remove,
                Label = ProjectViewModel.RemoveFileLabel(file),
                IsEnabled = model.Working is null,
            };
            remove.Click += (_, _) =>
            {
                model.AskRemoveFile(file);
                Refresh();
            };
            Grid.SetColumn(remove, column);
            row.Children.Add(remove);
        }

        return row;
    }

    private ListCard ConversationCard(ThreadSummary thread)
    {
        var card = new ListCard { Title = model.ThreadLabel(thread) };
        card.Opened += () => navigator.NavigateTo(Addresses.Conversation(thread.Id));
        if (model.CanMoveOut(thread.Id))
        {
            card.Trailing = More(thread.Id);
        }

        return card;
    }

    private Menu More(Guid threadId)
    {
        var menu = new Menu { Label = "More", TriggerIcon = IconName.More, ShowTriggerLabel = false };
        menu.Items.Add(TaskItem(ProjectViewModel.MoveOutLabel, () => model.MoveOut(threadId)));
        return menu;
    }

    private MenuItem TaskItem(string label, Func<Task> work)
    {
        var item = new MenuItem { Content = label };
        item.Selected += async (_, _) => await Run(work);
        return item;
    }

    private bool AskBeforeDiscard()
    {
        if (model.Editing && model.HasChanges)
        {
            model.CancelEditing();
            Refresh();
            return false;
        }

        return true;
    }

    private void DiscardEdits()
    {
        model.Discard();
        updating = true;
        try
        {
            Open(discardDialog, false);
            Open(editSheet, false);
        }
        finally
        {
            updating = false;
        }

        Refresh();
        editButton.TakeFocus();
    }

    private Task SaveFromSheet() => Run(model.Save);

    private async Task Run(Func<Task> work)
    {
        var pending = work();
        Refresh();
        await pending;
        Refresh();
    }

    private void Watch(Dialog dialog, Action closed)
    {
        dialog.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) =>
        {
            if (updating || dialog.Open)
            {
                return;
            }

            closed();
            Refresh();
        });
    }

    private static void Open(Dialog dialog, bool open)
    {
        if (dialog.Open != open)
        {
            dialog.Open = open;
        }
    }

    private static Grid Heading(string title, FrameworkElement? action)
    {
        var heading = new Grid { ColumnSpacing = Tokens.Number("DvSpace3") };
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var name = PageParts.CardTitle(title);
        Grid.SetColumn(name, 0);
        heading.Children.Add(name);
        if (action is not null)
        {
            Grid.SetColumn(action, 1);
            heading.Children.Add(action);
        }

        return heading;
    }

    private static TextBlock Selectable(string text)
    {
        var block = Styles.Text(text, "DvTextParagraph");
        block.IsTextSelectionEnabled = true;
        return block;
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

    private Task AddFile() => model.ChooseFiles(PickedFiles.ChooseAll);

    private static Task OpenDownload(string address)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            _ = Launcher.LaunchUriAsync(uri);
        }

        return Task.CompletedTask;
    }

    #endregion
}
