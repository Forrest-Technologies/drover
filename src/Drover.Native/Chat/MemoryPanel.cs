namespace Drover.Native.Chat;

/// <summary>
/// What a bot remembers about the person reading, beside its conversation. Every note is readable, says where it
/// came from, and is changed in place. The panel has no filter and no folders: the notes are few by design, and a
/// list short enough to read needs neither.
/// </summary>
public sealed class MemoryPanel : Grid
{
    #region Private Fields

    private readonly MemoryPanelViewModel model = new();

    private readonly Batch batch;

    private readonly StackPanel column = new() { Spacing = Tokens.Number("DvSpace4"), Padding = new(Tokens.Number("DvSpace4")) };

    private readonly TextBlock loading = Styles.Text(null, "DvTextSecondary");

    private readonly EmptyState failed = new() { Title = MemoryPanelViewModel.FailedTitle, ActionLabel = MemoryPanelViewModel.TryAgain };

    private readonly FocusLine off = new();

    private readonly Link turnOn = new() { Ink = "DvText", HoverInk = "DvText", MinHeight = Targets.Minimum };

    private readonly StatusNote saveProblem = new() { Kind = NoteKind.Danger };

    private readonly StackPanel add = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly TextArea addField = new() { Label = MemoryPanelViewModel.AddLabel, Rows = 2 };

    private readonly Button addButton = new() { Kind = ButtonKind.Primary, Content = MemoryPanelViewModel.AddAction, HorizontalAlignment = HorizontalAlignment.Left };

    private readonly StatusNote nearlyFull = new();

    private readonly TextBlock empty = Styles.Text(null, "DvTextSecondary");

    private readonly StackPanel notes = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly Dictionary<Guid, IconButton> changeButtons = [];

    private readonly Dictionary<Guid, Button> removeButtons = [];

    private TextArea? editField;

    // What the list was last drawn from: it is drawn again only when one of these changes, so a field a person is
    // typing in is never replaced under them.
    private (IReadOnlyList<MemoryNote> Notes, Guid? Editing, bool Changeable, string BotName)? drawn;

    #endregion

    #region Constructors

    public MemoryPanel()
    {
        batch = new(this, Apply, Draw);
        Pieces.SayWhenShown(loading);
        failed.Cause = model.FailedCause;
        failed.Action += (_, _) => model.TryAgainNow();

        addField.RegisterPropertyChangedCallback(TextArea.ValueProperty, (_, _) =>
        {
            model.Adding = addField.Value;
            addButton.IsEnabled = !string.IsNullOrWhiteSpace(model.Adding);
        });
        addButton.Click += async (_, _) => await model.Add();
        add.Children.Add(addField);
        add.Children.Add(addButton);

        column.Children.Add(loading);
        column.Children.Add(failed);
        column.Children.Add(off);
        column.Children.Add(turnOn);
        column.Children.Add(saveProblem);
        column.Children.Add(add);
        column.Children.Add(nearlyFull);
        column.Children.Add(empty);
        column.Children.Add(notes);
        Children.Add(new ScrollViewer { Content = column, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled, IsTabStop = false });

        model.Remove += id => Remove?.Invoke(this, id);
        model.Retry += () => Retry?.Invoke(this, EventArgs.Empty);
        model.PropertyChanged += (_, _) => batch.Draw();
        this.WhenShownOrRethemed(() =>
        {
            drawn = null;
            batch.Draw();
        });
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty StateProperty =
        Dp.Register<MemoryPanel, MemoryPanelState>(nameof(State), MemoryPanelState.Loading, panel => panel.batch.Values());

    public static readonly DependencyProperty BotNameProperty = Dp.Register<MemoryPanel, string>(nameof(BotName), string.Empty, panel => panel.batch.Values());

    public static readonly DependencyProperty NotesProperty =
        Dp.Register<MemoryPanel, IReadOnlyList<MemoryNote>>(nameof(Notes), [], panel => panel.batch.Values());

    public static readonly DependencyProperty NearlyFullProperty = Dp.Register<MemoryPanel, bool>(nameof(NearlyFull), false, panel => panel.batch.Values());

    public static readonly DependencyProperty ProblemProperty = Dp.Register<MemoryPanel, string?>(nameof(Problem), null, panel => panel.batch.Values());

    public static readonly DependencyProperty EditorHrefProperty = Dp.Register<MemoryPanel, string?>(nameof(EditorHref), null, panel => panel.batch.Values());

    public static readonly DependencyProperty OnAddProperty =
        Dp.Register<MemoryPanel, Func<string, Task<string?>>?>(nameof(OnAdd), null, panel => panel.batch.Values());

    public static readonly DependencyProperty OnChangeProperty =
        Dp.Register<MemoryPanel, Func<Guid, string, Task<string?>>?>(nameof(OnChange), null, panel => panel.batch.Values());

    /// <summary>What the panel has to show. Required.</summary>
    public MemoryPanelState State
    {
        get => (MemoryPanelState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>The bot whose notes these are. Required.</summary>
    public string BotName
    {
        get => (string)GetValue(BotNameProperty);
        set => SetValue(BotNameProperty, value);
    }

    /// <summary>The notes, newest first.</summary>
    public IReadOnlyList<MemoryNote> Notes
    {
        get => (IReadOnlyList<MemoryNote>)GetValue(NotesProperty);
        set => SetValue(NotesProperty, value);
    }

    public bool NearlyFull
    {
        get => (bool)GetValue(NearlyFullProperty);
        set => SetValue(NearlyFullProperty, value);
    }

    /// <summary>Why loading failed, as a sentence.</summary>
    public string? Problem
    {
        get => (string?)GetValue(ProblemProperty);
        set => SetValue(ProblemProperty, value);
    }

    /// <summary>The editor's Memory section, for a person who may change the bot. Null for everyone else.</summary>
    public string? EditorHref
    {
        get => (string?)GetValue(EditorHrefProperty);
        set => SetValue(EditorHrefProperty, value);
    }

    /// <summary>
    /// Saves a new note. Returns why it was not saved, or nothing when it was. The caller updates <see cref="Notes"/>
    /// itself once the note is saved.
    /// </summary>
    public Func<string, Task<string?>>? OnAdd
    {
        get => (Func<string, Task<string?>>?)GetValue(OnAddProperty);
        set => SetValue(OnAddProperty, value);
    }

    /// <summary>
    /// Saves a changed note. Returns why it was not saved, or nothing when it was. As with <see cref="OnAdd"/>, the
    /// caller updates <see cref="Notes"/> itself.
    /// </summary>
    public Func<Guid, string, Task<string?>>? OnChange
    {
        get => (Func<Guid, string, Task<string?>>?)GetValue(OnChangeProperty);
        set => SetValue(OnChangeProperty, value);
    }

    /// <summary>Removes at once. The caller offers Undo in a toast: a note is small and restoring it is exact.</summary>
    public event EventHandler<Guid>? Remove;

    public event EventHandler? Retry;

    #endregion

    #region Public Methods

    /// <summary>Moves the focus to a note's Change button once the note is shown, as after it is put back.</summary>
    public void FocusNote(Guid noteId) => model.FocusNote(noteId);

    #endregion

    #region Private Methods

    private void Apply()
    {
        (model.State, model.BotName, model.Notes, model.NearlyFull) = (State, BotName, Notes, NearlyFull);
        (model.Problem, model.EditorHref, model.OnAdd, model.OnChange) = (Problem, EditorHref, OnAdd, OnChange);
        model.ParametersSet();
    }

    private void Draw()
    {
        var (isLoading, isFailed, isOff) = (model.State is MemoryPanelState.Loading, model.State is MemoryPanelState.Failed, model.State is MemoryPanelState.Off);
        var isReady = model.State is MemoryPanelState.Ready;
        var listed = (isOff || isReady) && model.Notes.Count > 0;

        Show(loading, isLoading);
        loading.Text = model.LoadingHint;
        Show(failed, isFailed);
        failed.Cause = model.FailedCause;

        // Off, the bot keeps no new notes, and the ones it kept stay until the person removes them: listed, with
        // Remove and nothing else.
        Show(off, isOff);
        off.Say(model.OffHint);
        Show(turnOn, isOff && model.EditorHref is not null);
        turnOn.Text = model.TurnOnText;
        turnOn.Address = model.EditorHref;

        saveProblem.Text = isReady ? model.SaveProblem : null;
        Show(add, isReady);
        addField.Hint = model.AddHint;
        if (addField.Value != model.Adding)
        {
            addField.Value = model.Adding;
        }

        addButton.IsEnabled = !string.IsNullOrWhiteSpace(model.Adding);
        nearlyFull.Text = isReady ? model.NearlyFullNote : null;
        Show(empty, isReady && model.Notes.Count is 0);
        empty.Text = model.EmptyHint;
        Show(notes, listed);

        if (listed)
        {
            DrawNotes(changeable: isReady);
        }

        MoveFocus();
    }

    private static void Show(UIElement element, bool shown) => element.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

    // The notes, each with what it says, who wrote it and when, and the controls the state allows: Change and Remove
    // while memory is on, Remove alone while it is off.
    private void DrawNotes(bool changeable)
    {
        var wanted = (model.Notes, changeable ? model.Editing : null, changeable, model.BotName);
        if (drawn is { } last && ReferenceEquals(last.Notes, wanted.Notes) && (last.Editing, last.Changeable, last.BotName) == (wanted.Item2, changeable, model.BotName))
        {
            return;
        }

        drawn = wanted;
        notes.Children.Clear();
        changeButtons.Clear();
        removeButtons.Clear();
        editField = null;
        AutomationProperties.SetName(notes, model.ListName);

        foreach (var note in model.Notes)
        {
            var frame = new Facet { Cut = Tokens.Number("DvCutS"), Fill = Tokens.Brush(this, "DvBg2"), Padding = new(Tokens.Number("DvSpace3")) };
            frame.Children.Add(changeable && model.Editing == note.Id ? EditForm(note) : Shown(note, changeable));
            notes.Children.Add(frame);
        }
    }

    private StackPanel EditForm(MemoryNote note)
    {
        var field = new TextArea { Label = MemoryPanelViewModel.EditLabel, Rows = 3, Value = model.Draft };
        var save = new Button { Kind = ButtonKind.Primary, Content = MemoryPanelViewModel.SaveAction, IsEnabled = !string.IsNullOrWhiteSpace(model.Draft) };
        var cancel = new Button { Content = MemoryPanelViewModel.CancelAction };
        field.RegisterPropertyChangedCallback(TextArea.ValueProperty, (_, _) =>
        {
            model.Draft = field.Value;
            save.IsEnabled = !string.IsNullOrWhiteSpace(model.Draft);
        });
        save.Click += async (_, _) => await model.Save(note);
        cancel.Click += (_, _) => model.Cancel(note);
        editField = field;

        var form = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        form.Children.Add(field);
        form.Children.Add(Actions(save, cancel));
        return form;
    }

    private StackPanel Shown(MemoryNote note, bool changeable)
    {
        var origin = Styles.Text(model.Origin(note), "DvTextMono");
        if (note.OriginHref is { } moment)
        {
            var link = Pieces.LinkIn(this, MemoryPanelViewModel.OpenMoment, moment, "DvText2");
            AutomationProperties.SetName(link, model.OpenMomentName(note));
            origin.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "  " });
            origin.Inlines.Add(link);
        }

        var remove = new Button { Content = MemoryPanelViewModel.RemoveAction };
        AutomationProperties.SetName(remove, MemoryPanelViewModel.RemoveName(note));
        remove.Click += (_, _) => model.RemoveNote(note);
        removeButtons[note.Id] = remove;

        var actions = Actions(remove);
        if (changeable)
        {
            // Changing a note is known by the edit icon; its name says which note, also as its tooltip.
            var change = new IconButton { Icon = IconName.Edit, Label = MemoryPanelViewModel.ChangeName(note) };
            change.Click += (_, _) => model.Edit(note);
            changeButtons[note.Id] = change;
            actions.Children.Insert(0, change);
        }

        var shown = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        shown.Children.Add(Styles.Text(note.Text));
        shown.Children.Add(origin);
        shown.Children.Add(actions);
        return shown;
    }

    private static FlowPanel Actions(params Button[] buttons)
    {
        var row = new FlowPanel { Gap = Tokens.Number("DvSpace2") };
        foreach (var button in buttons)
        {
            row.Children.Add(button);
        }

        return row;
    }

    // Where the focus goes once the panel has drawn: into the field, onto a note's Change button, or to adding.
    private void MoveFocus()
    {
        switch (model.TakeFocus())
        {
            case { EditField: true } when editField is { } field:
                Pieces.FocusWhenShown(field, field.TakeFocus);
                break;
            case { AddField: true } when model.State is MemoryPanelState.Off:
                Pieces.FocusWhenShown(off, off.TakeFocus);
                break;
            case { AddField: true }:
                Pieces.FocusWhenShown(addField, addField.TakeFocus);
                break;
            case { Note: { } id } when (changeButtons.GetValueOrDefault(id) as Pressable ?? removeButtons.GetValueOrDefault(id)) is { } button:
                Pieces.FocusWhenShown(button, button.TakeFocus);
                break;
        }
    }

    #endregion
}
