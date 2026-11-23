namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// The field a person writes to a bot in, and what stands beside it: Send, Stop while the bot works, the files
/// chosen for the message, and the row of options behind the plus button. The row offers the model the
/// conversation answers with when the bot may use more than one. The conversation around it says what is going on
/// and hears what the person does.
/// </summary>
public sealed partial class ComposerViewModel : ObservableObject
{
    #region Private Fields

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FieldName), nameof(Placeholder))]
    private string botName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSend), nameof(HasTwoActions), nameof(IsWorking))]
    private bool running;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorking))]
    private bool waiting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private bool disabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private bool sending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSomething), nameof(CanSend), nameof(ShowsSend), nameof(HasTwoActions))]
    private IReadOnlyList<AttachmentItem> attachments = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSomething), nameof(CanSend), nameof(ShowsSend), nameof(HasTwoActions))]
    private string text = string.Empty;

    [ObservableProperty]
    private bool optionsOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModelChoice))]
    private IReadOnlyList<ModelOption> modelOptions = [];

    [ObservableProperty]
    private string chosenModel = BotsOwnModel;

    #endregion

    #region Properties

    /// <summary>The most files taken from one choice, paste or drop. A person who hands over more is told.</summary>
    public const int MaxFilesAtOnce = 100;

    /// <summary>What the person wrote, trimmed, when they send it.</summary>
    public Func<string, Task>? OnSend { get; set; }

    /// <summary>Files the person chose, pasted, or dropped.</summary>
    public Func<IReadOnlyList<IPickedFile>, Task>? OnFiles { get; set; }

    /// <summary>A sentence for the person about what happened to what they chose, pasted, or dropped.</summary>
    public Action<string, ToastKind>? OnNotice { get; set; }

    /// <summary>A chosen file taken off before sending.</summary>
    public Func<Guid, Task>? OnRemoveFile { get; set; }

    /// <summary>The model the person chose for the conversation's next replies, or null for the bot's own.</summary>
    public Func<string?, Task>? OnModelChosen { get; set; }

    /// <summary>Stops the whole run, including work it handed to other bots.</summary>
    public Func<Task>? OnCancel { get; set; }

    /// <summary>The field's accessible name.</summary>
    public string FieldName => $"Message to {BotName}";

    public string Placeholder => $"Write to {BotName}";

    public bool HasSomething => !string.IsNullOrWhiteSpace(Text) || Attachments.Count > 0;

    /// <summary>Send waits until every file is uploaded, and a file that cannot go holds the message until it is taken off.</summary>
    public bool CanSend => !Disabled && !Sending && HasSomething && FileWords.CanSend(Attachments);

    /// <summary>Stop takes the place of Send while a run is active.</summary>
    public bool ShowsStop => Running;

    /// <summary>Send comes back beside Stop once there is something written: a message sent now reaches the bot at its next step.</summary>
    public bool ShowsSend => !Running || HasSomething;

    /// <summary>True while Stop and Send stand side by side, which a narrow window lays out on two rows.</summary>
    public bool HasTwoActions => Running && HasSomething;

    /// <summary>True while the bot works and does not wait for a person: the line travels the top edge then.</summary>
    public bool IsWorking => Running && !Waiting;

    /// <summary>The value of the option that answers with the bot's own model.</summary>
    public static string BotsOwnModel => string.Empty;

    public static string ModelLabel => "Model for this conversation";

    /// <summary>The choice is offered only when there is one to make.</summary>
    public bool HasModelChoice => ModelOptions.Count > 1;

    /// <summary>Raised to put the cursor in the message field.</summary>
    public event Action? FocusRequested;

    /// <summary>Raised when the row of options opened, to put the focus on its first option.</summary>
    public event Action? OptionsFocusRequested;

    #endregion

    #region Public Methods

    /// <summary>Puts words in the message field for the person to read and change. Nothing is sent.</summary>
    public void Write(string words)
    {
        Text = words;
        Focus();
    }

    /// <summary>Puts the cursor back in the message field.</summary>
    public void Focus() => FocusRequested?.Invoke();

    /// <summary>Opening the row by keyboard puts focus on its first option, since the row comes before the plus button.</summary>
    [RelayCommand]
    public void ToggleOptions()
    {
        OptionsOpen = !OptionsOpen;
        if (OptionsOpen)
        {
            OptionsFocusRequested?.Invoke();
        }
    }

    /// <summary>
    /// Shows what the server said the conversation may answer with: the bot's own model first, then the others it may
    /// choose. Nothing is offered while that is not known.
    /// </summary>
    public void ShowModels(ConversationModelChoice? choice)
    {
        ModelOptions = choice is null
            ? []
            :
            [
                new(BotsOwnModel, $"{choice.BotModelName} (the bot's own)"),
                .. choice.Models.Where(option => option.Model != choice.BotModel).Select(option => new ModelOption(option.Model, option.Name)),
            ];
        ChosenModel = choice?.Chosen ?? BotsOwnModel;
    }

    /// <summary>The person picked a model. The conversation asks the server, which may refuse it.</summary>
    public Task ChooseModel(string value)
    {
        if (value == ChosenModel)
        {
            return Task.CompletedTask;
        }

        ChosenModel = value;
        return OnModelChosen?.Invoke(value == BotsOwnModel ? null : value) ?? Task.CompletedTask;
    }

    /// <summary>Tells the person something about the files they handed over, in one sentence.</summary>
    public void Notify(string words, ToastKind kind) => OnNotice?.Invoke(words, kind);

    /// <summary>Chosen with Attach, pasted into the field, or dropped on the conversation: every file comes through here.</summary>
    public Task Chosen(IReadOnlyList<IPickedFile> files)
    {
        if (files.Count > MaxFilesAtOnce)
        {
            Notify(FileWords.OnlyTheFirstFilesTaken(MaxFilesAtOnce), ToastKind.Warning);
        }

        return OnFiles?.Invoke([.. files.Take(MaxFilesAtOnce)]) ?? Task.CompletedTask;
    }

    public Task Remove(Guid id) => OnRemoveFile?.Invoke(id) ?? Task.CompletedTask;

    [RelayCommand]
    public Task Cancel() => OnCancel?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    public async Task Send()
    {
        if (!CanSend)
        {
            return;
        }

        var message = Text.Trim();
        Text = string.Empty;
        if (OnSend is { } send)
        {
            await send(message);
        }
    }

    #endregion
}
