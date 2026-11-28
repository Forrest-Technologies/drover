namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// The field a person writes to a bot in, and what stands beside it: Send, Stop while the bot works, the files
/// chosen for the message, and the models the conversation may answer with. The conversation around it says what is going on
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

    // Why the field cannot take a message, read by assistive technology while it is disabled.
    [ObservableProperty]
    private string fieldDescription = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private bool sending;

    private bool takesFiles;

    private bool dropping;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSomething), nameof(CanSend), nameof(ShowsSend), nameof(HasTwoActions))]
    private IReadOnlyList<AttachmentItem> attachments = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSomething), nameof(CanSend), nameof(ShowsSend), nameof(HasTwoActions))]
    private string text = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModelChoice))]
    private IReadOnlyList<ModelOption> modelOptions = [];

    [ObservableProperty]
    private string chosenModel = BotsOwnModel;

    [ObservableProperty]
    private bool choosingModel;

    #endregion

    #region Properties

    /// <summary>The most files taken from one choice, paste or drop. A person who hands over more is told.</summary>
    public const int MaxFilesAtOnce = 100;

    /// <summary>What the person wrote, trimmed, when they send it. True means the server took the message.</summary>
    public Func<string, Task<bool>>? OnSend { get; set; }

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

    /// <summary>True while the bot works and does not wait for a person: the line travels around the composer then.</summary>
    public bool IsWorking => Running && !Waiting;

    /// <summary>The value of the option that answers with the bot's own model.</summary>
    public static string BotsOwnModel => string.Empty;

    public string? ActiveModelName { get; private set; }

    public string? ModelConnectionName { get; set; }

    public Func<IReadOnlyList<string>>? RecentModels { get; set; }

    public static string ModelLabel => "Model for this conversation";

    /// <summary>The choice is offered only when there is one to make.</summary>
    public bool HasModelChoice => ModelOptions.Count > 1;

    /// <summary>Whether the conversation takes files now. Attach, a paste of files and a drop all follow it.</summary>
    public bool TakesFiles
    {
        get => takesFiles;
        set
        {
            if (SetProperty(ref takesFiles, value) && !value)
            {
                ShowDropping(false);
            }
        }
    }

    /// <summary>True while files that this conversation would take are held over it.</summary>
    public bool Dropping => dropping;

    /// <summary>Raised to put the cursor in the message field.</summary>
    public event Action? FocusRequested;

    #endregion

    #region Public Methods

    /// <summary>Puts words in the message field for the person to read and change. Nothing is sent.</summary>
    public void Write(string words)
    {
        Text = words;
        Focus();
    }

    /// <summary>
    /// Adds the words of a recording after what is already written, for the person to read and change. Nothing is
    /// sent.
    /// </summary>
    public void Dictate(string words)
    {
        Text = string.IsNullOrWhiteSpace(Text) ? words : $"{Text.TrimEnd()} {words}";
        Focus();
    }

    /// <summary>Puts the cursor back in the message field.</summary>
    public void Focus() => FocusRequested?.Invoke();

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
                new(BotsOwnModel, "Use the bot's default"),
                .. choice.Models.Where(option => option.Model != choice.BotModel).Select(option => new ModelOption(option.Model, option.Name)),
            ];
        ChosenModel = choice?.Chosen ?? BotsOwnModel;
        ActiveModelName = choice is null ? null
            : ChosenModel == BotsOwnModel ? choice.BotModelName
            : ModelOptions.FirstOrDefault(option => option.Value == ChosenModel)?.Label ?? ChosenModel;
        OnPropertyChanged(nameof(ActiveModelName));
    }

    /// <summary>The person picked a model. The conversation asks the server, which may refuse it.</summary>
    public async Task ChooseModel(string value)
    {
        if (ChoosingModel || value == ChosenModel)
        {
            return;
        }

        ChoosingModel = true;
        try
        {
            ChosenModel = value;
            await (OnModelChosen?.Invoke(value == BotsOwnModel ? null : value) ?? Task.CompletedTask);
        }
        finally
        {
            ChoosingModel = false;
        }
    }

    /// <summary>Tells the person something about the files they handed over, in one sentence.</summary>
    public void Notify(string words, ToastKind kind) => OnNotice?.Invoke(words, kind);

    /// <summary>
    /// Chosen with Attach, pasted into the field, or dropped on the conversation: every file comes through here. More
    /// than one go takes are cut to the first ones, and the person is told.
    /// </summary>
    public Task Chosen(IReadOnlyList<IPickedFile> files) =>
        Hand(new([.. files.Take(MaxFilesAtOnce)], Beyond: Math.Max(0, files.Count - MaxFilesAtOnce)));

    /// <summary>
    /// What one choice, paste or drop handed over. Its files go on; what was left out of it is said in one sentence:
    /// files past the most one go takes, or folders when they were all it held.
    /// </summary>
    public Task Hand(HandedFiles handed)
    {
        if (handed.Files.Count == 0)
        {
            if (handed.Folders > 0)
            {
                Notify(FileWords.FoldersOnly, ToastKind.Warning);
            }

            return Task.CompletedTask;
        }

        if (handed.Beyond > 0)
        {
            Notify(FileWords.OnlyTheFirstFilesTaken(handed.Files.Count), ToastKind.Warning);
        }

        return OnFiles?.Invoke(handed.Files) ?? Task.CompletedTask;
    }

    /// <summary>
    /// A paste into the message field, told what the device's clipboard holds. True when it holds files or a picture
    /// to take, so the field leaves the paste alone and <see cref="TakePasted"/> takes them. Text is what was meant
    /// whenever it is there, and an empty clipboard says nothing. A clipboard that could not be read, or a picture the
    /// device lists and did not hand over, is said in one sentence. A conversation that takes no files leaves every
    /// paste to the field.
    /// </summary>
    public bool Pasting(Func<ClipboardHolds> look, ILogger logger)
    {
        if (!TakesFiles)
        {
            return false;
        }

        ClipboardHolds holds;
        try
        {
            holds = look();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogError(exception, "The clipboard could not be read for a paste");
            Notify(FileWords.PasteFailed, ToastKind.Error);
            return false;
        }

        if (holds is ClipboardHolds.PictureNotHandedOver)
        {
            Notify(FileWords.PictureNotHandedOver, ToastKind.Warning);
        }

        return holds is ClipboardHolds.Files;
    }

    /// <summary>Takes the files of a paste. What could not be read is said in one sentence.</summary>
    public Task TakePasted(Func<Task<HandedFiles>> read, ILogger logger) =>
        Take(read, FileWords.PasteFailed, "pasted", logger);

    /// <summary>Shows or clears the drop line while files are held over the conversation.</summary>
    public void ShowDropping(bool value) => SetProperty(ref dropping, value && TakesFiles, nameof(Dropping));

    /// <summary>Clears the drop line when a drag ends without a drop, without attaching anything.</summary>
    public void CancelDropping() => ShowDropping(false);

    /// <summary>Takes the files dropped on the conversation. What could not be read is said in one sentence.</summary>
    public Task TakeDropped(Func<Task<HandedFiles>> read, ILogger logger)
    {
        ShowDropping(false);
        return Take(read, FileWords.DropFailed, "dropped", logger);
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
        if (OnSend is not { } send)
        {
            return;
        }

        var delivered = false;
        try
        {
            delivered = await send(message);
        }
        finally
        {
            if (!delivered)
            {
                Text = Text.Length == 0 ? message : $"{message}\n\n{Text}";
                Focus();
            }
        }
    }

    #endregion

    #region Private Methods

    // Reading what was handed over can take a while. A conversation that stops taking files meanwhile (it was left,
    // or could not be read) gets none of them.
    private async Task Take(Func<Task<HandedFiles>> read, string failed, string way, ILogger logger)
    {
        if (!TakesFiles)
        {
            return;
        }

        HandedFiles handed;
        try
        {
            handed = await read();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogError(exception, "What was {Way} could not be read as files", way);
            Notify(failed, ToastKind.Error);
            return;
        }

        if (TakesFiles)
        {
            await Hand(handed);
        }
    }

    #endregion
}
