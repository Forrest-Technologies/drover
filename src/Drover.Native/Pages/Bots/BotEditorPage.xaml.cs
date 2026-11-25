namespace Drover.Native.Pages;

/// <summary>
/// The bot editor. Its job: make one bot yours, its name, character, purpose, and instructions, then open only the
/// section you need. The definition is saved together; tools, rules, and schedules are saved as each is changed.
/// </summary>
public sealed partial class BotEditorPage : Screen
{
    #region Private Fields

    private readonly HiddenStatus saveWordsSaid = new();

    private readonly ScreenHeader header = new() { BackLabel = BotEditorViewModel.Back, BackAddress = Addresses.Bots };

    private readonly Grid identity = new() { ColumnSpacing = Tokens.Number("DvSpace4") };

    private readonly Avatar avatar = new() { Size = 96 };

    private readonly Field name = new() { Label = BotEditorViewModel.NameLabel, MaxLength = FieldLimits.BotName };

    private readonly Field handle = new() { Label = BotEditorViewModel.HandleLabel, Hint = BotEditorViewModel.HandleHint, MaxLength = FieldLimits.BotHandle };

    private readonly FlowPanel handleRow = new() { Gap = Tokens.Number("DvSpace2") };

    private readonly TextBlock handleWords = BotParts.Mono().Inked("DvText");

    private readonly Badge paused = new() { Text = BotEditorViewModel.PausedBadge, Kind = BadgeKind.Warn };

    private readonly Menu menu = new()
    {
        Label = BotEditorViewModel.MenuLabel,
        TriggerIcon = IconName.More,
        ShowTriggerLabel = false,
    };

    private readonly MenuItem pauseOrResume = MenuEntry(string.Empty);

    private readonly StatusNote actionProblem = new() { Kind = NoteKind.Danger };

    private readonly TextArea purpose = new() { Label = BotEditorViewModel.PurposeLabel, Rows = 2, Hint = BotEditorViewModel.PurposeHint };

    private readonly TextArea instructions = new() { Label = BotEditorViewModel.InstructionsLabel, Rows = 8, Hint = BotEditorViewModel.InstructionsHint };

    private readonly Dictionary<string, (Disclosure Frame, EditorPart Part)> sections = [];

    private readonly Facet saveBar = new();

    private readonly Grid saveLine = new();

    private readonly StackPanel saveState = new() { Spacing = Tokens.Number("DvSpace1") };

    private readonly StatusNote saveProblem = new() { Kind = NoteKind.Danger };

    private readonly TextBlock saveWords = BotParts.Hint();

    private readonly Button discard = new() { Content = BotEditorViewModel.DiscardLabel };

    private readonly Button save = new() { Kind = ButtonKind.Primary };

    private readonly Dialog removing = new();

    private readonly TextBlock beneathWords = Styles.Text(null, "DvTextParagraph");

    private readonly StackPanel removeActions = new() { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };

    private readonly Dialog leaving = new() { Title = BotEditorViewModel.LeaveTitle };

    private readonly TextBlock leaveCause = Styles.Text(null, "DvTextParagraph");

    private bool built;

    private bool showing;

    private bool phone;

    private string? shownTitle;

    private string? removeChoices;

    #endregion

    #region Constructors

    public BotEditorPage()
    {
        InitializeComponent();
        header.Actions.Add(menu);
        Layout.Body.Insert(0, header);
        Loading.Text = BotEditorViewModel.Loading;
        Model.PropertyChanged += (_, _) => Show();
        Unloaded += (_, _) => Model.Close();
        Show();
    }

    #endregion

    #region Properties

    public override string Title => Model.Title;

    public BotEditorViewModel Model { get; } = Make<BotEditorViewModel>();

    #endregion

    #region Private Methods

    protected override async Task Opened(RouteMatch<Type> route)
    {
        if (route.GuidParameter("BotId") is { } botId)
        {
            await Model.Open(botId, route.QueryValue("section"));
        }
        else
        {
            await Model.OpenUnmade(Guid.TryParse(route.QueryValue("parent"), out var parent) ? parent : null);
        }
    }

    private void Show()
    {
        var state = Model.State;
        ShowStage(state);
        header.Title = state is BotEditorState.Shown ? Model.ShownName : "Bot";
        menu.Shown(state is BotEditorState.Shown && !Model.IsNew);
        Loading.Shown(state is BotEditorState.Loading);
        Form.Shown(state is BotEditorState.Shown);

        if (state is BotEditorState.Shown)
        {
            Build();
            showing = true;
            try
            {
                ShowForm();
            }
            finally
            {
                showing = false;
            }
        }

        if (shownTitle != Title && ((App)Application.Current).MainWindow is { } window && Frame?.Content == this)
        {
            shownTitle = Title;
            PlatformShell.SetTitle(window, Title);
        }
    }

    private void ShowStage(BotEditorState state)
    {
        var wanted = state switch
        {
            BotEditorState.Failed => BotEditorViewModel.LoadFailedTitle,
            BotEditorState.NotPermitted => BotEditorViewModel.NotPermittedTitle,
            BotEditorState.Missing => BotEditorViewModel.MissingTitle,
            _ => null,
        };

        Stage.Shown(wanted is not null);

        if ((Stage.Content as EmptyState)?.Title == wanted && (state is not BotEditorState.Failed || ((EmptyState)Stage.Content!).Cause == Model.LoadProblem))
        {
            return;
        }

        Stage.Content = state switch
        {
            BotEditorState.Failed => Failed(),
            BotEditorState.NotPermitted => new EmptyState
            {
                Title = BotEditorViewModel.NotPermittedTitle,
                Cause = BotEditorViewModel.NotPermittedCause,
                ActionLabel = BotEditorViewModel.NotPermittedAction,
                ActionHref = BotEditorViewModel.NotPermittedAddress,
            },
            BotEditorState.Missing => new EmptyState
            {
                Title = BotEditorViewModel.MissingTitle,
                Cause = BotEditorViewModel.MissingCause,
                ActionLabel = BotEditorViewModel.MissingAction,
                ActionHref = BotEditorViewModel.MissingAddress,
            },
            _ => null,
        };
        Stage.Shown(Stage.Content is not null);
    }

    private EmptyState Failed()
    {
        var failed = new EmptyState { Title = BotEditorViewModel.LoadFailedTitle, Cause = Model.LoadProblem ?? string.Empty, ActionLabel = BotEditorViewModel.TryAgain };
        failed.Action += (_, _) => _ = Model.Load();
        return failed;
    }

    #endregion

    #region Building

    // The form is built once the bot is read: its parts then stay, and each change only shows what differs.
    private void Build()
    {
        if (built)
        {
            return;
        }

        built = true;
        var column = new StackPanel { Spacing = Tokens.Number("DvSpace5") };
        column.Children.Add(Identity());
        column.Children.Add(actionProblem);
        column.Children.Add(purpose.OnChanged(() => showing, value => Model.Edit(draft => draft.DescriptionForBots = value)));
        column.Children.Add(instructions.OnChanged(() => showing, value => Model.Edit(draft => draft.Instructions = value)));
        column.Children.Add(Sections());
        column.Children.Add(SaveBar());
        Form.Children.Add(column);
        Form.Children.Add(saveWordsSaid);
        Form.Children.Add(Removing());
        Form.Children.Add(Leaving());
        StickyBottom.Hold(saveBar, "DvSpace3");
    }

    private Grid Identity()
    {
        name.OnChanged(() => showing, Model.Rename).Submitted += (_, _) => _ = Model.Save();
        handle.OnChanged(() => showing, value => Model.Edit(draft => draft.Handle = value)).Submitted += (_, _) => _ = Model.Save();
        var changeHandle = new IconButton { Icon = IconName.Edit, Label = BotEditorViewModel.ChangeHandleLabel };
        changeHandle.Click += (_, _) =>
        {
            Model.ChangeHandle();
            handle.TakeFocus();
        };
        handleWords.VerticalAlignment = paused.VerticalAlignment = VerticalAlignment.Center;
        handleRow.Children.Add(handleWords);
        handleRow.Children.Add(changeHandle);
        handleRow.Children.Add(paused);
        menu.Items.Add(MenuEntry(BotEditorViewModel.DuplicateLabel, () => _ = Model.Duplicate()));
        menu.Items.Add(MenuEntry(BotEditorViewModel.MakeExtensionLabel, Model.MakeExtension));
        menu.Items.Add(pauseOrResume);
        menu.Items.Add(MenuEntry(BotEditorViewModel.RemoveLabel, Model.AskToRemove));
        pauseOrResume.Selected += (_, _) => _ = Model.IsPaused ? Model.Resume() : Model.Pause();

        // The character's own controls, re-roll and part swapping, take their place beside it here.
        var names = BotParts.Column("DvSpace2", name, handle, handleRow);
        identity.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        identity.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        identity.RowDefinitions.Add(new() { Height = GridLength.Auto });
        avatar.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(names, 1);
        identity.Children.Add(avatar);
        identity.Children.Add(names);
        identity.SizeChanged += (_, _) => Fit();
        Fit();
        return identity;
    }

    private void Fit()
    {
        var narrow = identity.ActualWidth > 0 && identity.ActualWidth < (double)Styles.Dictionary["DvBreakPhone"];
        if (narrow == phone && identity.ActualWidth > 0)
        {
            return;
        }

        phone = narrow;
        FitSaveBar();
    }

    private StackPanel Sections()
    {
        var column = new StackPanel { Spacing = Tokens.Number("DvSpace3") };

        foreach (var section in BotEditorViewModel.Sections)
        {
            EditorPart part = section switch
            {
                BotEditorViewModel.ModelSection => new ModelSectionView(Model),
                BotEditorViewModel.ToolsSection => new ToolsSectionView(Model),
                BotEditorViewModel.RulesSection => new RulesSectionView(Model),
                BotEditorViewModel.MemorySection => new MemorySectionView(Model),
                BotEditorViewModel.RoutinesSection => new RoutinesSectionView(Model),
                BotEditorViewModel.SandboxSection => new SandboxSectionView(Model),
                BotEditorViewModel.TeamSection => new TeamSectionView(Model),
                _ => new LimitsSectionView(Model),
            };
            var frame = new Disclosure { Title = section, Body = part };
            frame.RegisterPropertyChangedCallback(Disclosure.OpenProperty, (_, _) =>
            {
                if (!showing && frame.Open != Model.IsOpen(section))
                {
                    Model.Toggle(section, frame.Open);
                }
            });
            sections[section] = (frame, part);
            column.Children.Add(frame);
        }

        return column;
    }

    // Saving stays in reach while the page scrolls.
    private Facet SaveBar()
    {
        saveBar.Role = FacetRole.Frame;
        saveBar.WhenShownOrRethemed(() => (saveBar.Edge, saveBar.Fill) = (Tokens.Brush(saveBar, "DvText3"), Tokens.Brush(saveBar, "DvBg2")));
        AutomationProperties.SetAccessibilityView(saveWords, AccessibilityView.Raw);
        saveState.Children.Add(saveProblem);
        saveState.Children.Add(saveWords);
        saveState.VerticalAlignment = VerticalAlignment.Center;
        discard.Click += (_, _) => Model.Discard();
        save.Click += (_, _) => _ = Model.Save();
        discard.VerticalAlignment = save.VerticalAlignment = VerticalAlignment.Center;
        saveLine.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        saveLine.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        saveLine.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        saveLine.RowDefinitions.Add(new() { Height = GridLength.Auto });
        saveLine.RowDefinitions.Add(new() { Height = GridLength.Auto });
        saveLine.Children.Add(saveState);
        saveLine.Children.Add(discard);
        saveLine.Children.Add(save);
        saveBar.Children.Add(saveLine);
        FitSaveBar();
        return saveBar;
    }

    // On a phone the words give way to the two buttons, so the bar is one row; a problem takes a row of its own.
    private void FitSaveBar()
    {
        var problemRow = phone && Model.SaveProblem is not null;
        var gap = Tokens.Number(phone ? "DvSpace2" : "DvSpace3");
        saveBar.Padding = phone ? new(Tokens.Number("DvSpace2")) : Tokens.Space("DvSpace4", "DvSpace3");
        saveLine.ColumnSpacing = gap;
        saveLine.RowSpacing = problemRow ? Tokens.Number("DvSpace2") : 0;
        Grid.SetColumnSpan(saveState, problemRow ? 3 : 1);
        Grid.SetRow(discard, problemRow ? 1 : 0);
        Grid.SetRow(save, problemRow ? 1 : 0);
        Grid.SetColumn(discard, 1);
        Grid.SetColumn(save, 2);
    }

    private Dialog Removing()
    {
        removing.Body = BotParts.Column("DvSpace3", Styles.Text(BotEditorViewModel.RemoveCause, "DvTextParagraph"), beneathWords);
        removing.Actions = removeActions;
        removing.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) =>
        {
            if (!showing && removing.Open != Model.Removing)
            {
                Model.SetRemoving(removing.Open);
            }
        });
        return removing;
    }

    private Dialog Leaving()
    {
        leaving.Body = leaveCause;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        actions.Children.Add(BotParts.Action(BotEditorViewModel.StayHere, () => Model.SetLeaving(false)));
        actions.Children.Add(BotParts.Action(BotEditorViewModel.LeaveLabel, Model.Leave, ButtonKind.Danger));
        leaving.Actions = actions;
        leaving.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) =>
        {
            if (!showing && leaving.Open != Model.Leaving)
            {
                Model.SetLeaving(leaving.Open);
            }
        });
        return leaving;
    }

    private static MenuItem MenuEntry(string label, Action? choose = null)
    {
        var item = new MenuItem { Content = label, HorizontalContentAlignment = HorizontalAlignment.Left };
        if (choose is not null)
        {
            item.Selected += (_, _) => choose();
        }

        return item;
    }

    #endregion

    #region Showing

    private void ShowForm()
    {
        var draft = Model.Draft!;
        avatar.BotName = Model.ShownName;
        avatar.Hue = Model.Hue;
        name.Show(draft.Name);
        handle.Shown(Model.EditingHandle).Show(draft.Handle);
        handleRow.Shown(!Model.EditingHandle);
        handleWords.Show(Model.HandleShown);
        paused.Shown(Model.IsPaused);
        pauseOrResume.Content = Model.IsPaused ? BotEditorViewModel.ResumeLabel : BotEditorViewModel.PauseLabel;
        actionProblem.Show(Model.ActionProblem);
        purpose.Show(draft.DescriptionForBots);
        instructions.Show(draft.Instructions);
        ShowSections();
        ShowSaving();
        ShowDialogs();
    }

    private void ShowSections()
    {
        foreach (var (section, (frame, part)) in sections)
        {
            frame.Shown(Model.ShowsSection(section));

            if (!Model.ShowsSection(section))
            {
                continue;
            }

            frame.Summary = Model.Summary(section);
            if (frame.Open != Model.IsOpen(section))
            {
                frame.Open = Model.IsOpen(section);
            }

            if (frame.Open)
            {
                part.Show();
            }
        }
    }

    private void ShowSaving()
    {
        // The words are always in the page, so a change of state is read out; the bar shows when there is something to act on.
        saveWordsSaid.Say(Model.SaveWords);
        saveBar.Shown(Model.ShowsSaveBar);
        saveProblem.Show(Model.SaveProblem);
        saveWords.Show(Model.SaveWords);
        discard.IsEnabled = !Model.Saving;
        save.IsEnabled = Model.CanSave;
        save.Content = Model.SaveLabel;
        FitSaveBar();
    }

    private void ShowDialogs()
    {
        removing.Title = Model.RemoveTitle;
        beneathWords.Shown(Model.HasBeneath).Show(Model.HasBeneath ? Model.BeneathWords : null);
        var choices = Model.HasBeneath ? Model.RemoveAllWords : string.Empty;

        if (removeChoices != choices)
        {
            removeChoices = choices;
            removeActions.Fill(Model.HasBeneath
                ?
                [
                    BotParts.Action(BotEditorViewModel.KeepBot, () => Model.SetRemoving(false)),
                    BotParts.Action(BotEditorViewModel.RemoveKeepRest, () => _ = Model.Remove(false), ButtonKind.Danger),
                    BotParts.Action(Model.RemoveAllWords, () => _ = Model.Remove(true), ButtonKind.Danger),
                ]
                :
                [
                    BotParts.Action(BotEditorViewModel.KeepBot, () => Model.SetRemoving(false)),
                    BotParts.Action(BotEditorViewModel.RemoveBot, () => _ = Model.Remove(null), ButtonKind.Danger),
                ]);
        }

        removing.Open = Model.Removing;
        leaveCause.Show(Model.LeaveCause);
        leaving.Open = Model.Leaving;
    }

    #endregion
}
