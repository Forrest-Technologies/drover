namespace Drover.Presentation.ViewModels;

/// <summary>
/// The extension editor. Its job: write a bot down so someone else can import it, and hand over the file. The form
/// comes first; the same file as text is one tab away, and the server reads that text whenever it becomes the form.
/// </summary>
public sealed partial class ExtensionEditorViewModel(
    DroverApi api,
    SessionState session,
    ExtensionHandoff handoff,
    Navigator navigator,
    ILogger<ExtensionEditorViewModel> logger) : ObservableObject
{
    #region Private Fields

    public const string FormTab = "form";

    public const string JsonTab = "json";

    private ExtensionDraft draft = new();

    private bool opened;

    private Guid? botId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsJson))]
    private string tab = FormTab;

    [ObservableProperty]
    private string text = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shows))]
    private string? loadProblem;

    [ObservableProperty]
    private string? notice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoticeKind))]
    private bool noticeIsProblem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckLabel), nameof(Idle))]
    private bool busy;

    #endregion

    #region Properties

    public static string Title => "Make an extension";

    public static string PageTitle => "Make an extension · Drover";

    public static string BackLabel => "Back to extensions";

    public static string OpeningNote => "Opening the bot.";

    public static string LoadFailedTitle => "Drover could not open this bot as an extension";

    public static string MemberNote => "An owner or admin of the space can make an extension.";

    public static string Lead =>
        "Save a bot as a file that someone else can import, with the tool server it uses if it has one. The file holds text and an address. It holds no keys, grants or notes.";

    public static string TabsLabel => "How the extension is shown";

    public static IReadOnlyList<TabItem> Tabs { get; } = [new(FormTab, "Form"), new(JsonTab, "JSON")];

    public static IReadOnlyList<SegmentOption<string>> AuthKinds { get; } =
        [new("none", "None"), new("header", "Header"), new("oauth", "Sign-in")];

    public static string TextLabel => "The file as text";

    public static string TextHint => "Changes here also change the form.";

    public static string AuthLabel => "How Drover signs in to this server";

    public static string ServerLead => "The file says where the server is. It never holds a key: whoever imports it types their own.";

    public static string CopyLabel => "Copy its text";

    public static string ImportLabel => "Import into this space";

    public Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    // The server decides this for every request. Here it only keeps controls a person cannot use off the screen.
    public bool CanEdit => session.Space?.Role is "owner" or "admin";

    /// <summary>The editor has what it edits: a form to fill, once the bot (if any) is opened.</summary>
    public bool IsOpen => opened;

    /// <summary>Nothing is being asked of the server, so the form can be used.</summary>
    public bool Idle => !Busy;

    /// <summary>What fills the screen under its header, in the order the screen decides it.</summary>
    public ExtensionEditorContent Shows =>
        LoadProblem is not null ? ExtensionEditorContent.LoadFailed
        : !opened ? ExtensionEditorContent.Opening
        : !CanEdit ? ExtensionEditorContent.Member
        : ExtensionEditorContent.Form;

    public bool ShowsJson => Tab == JsonTab;

    public string CheckLabel => Busy ? "Checking" : "Check the file";

    public NoteKind NoticeKind => NoticeIsProblem ? NoteKind.Danger : NoteKind.Ok;

    /// <summary>The file as it stands now: the text being edited, or the form written out.</summary>
    public string CurrentText => Tab == JsonTab ? Text : ExtensionFile.Write(Draft.ToPackage());

    public string FileName => $"{(string.IsNullOrWhiteSpace(draft.Id) ? "extension" : draft.Id.Trim())}.json";

    private ExtensionDraft Draft => draft;


    public string Name
    {
        get => Draft.Name;
        set => Say(() => Draft.Rename(value), nameof(Name), nameof(Id));
    }

    public string Id
    {
        get => Draft.Id;
        set => Say(() => Draft.ChooseId(value), nameof(Id), when: value != Draft.Id);
    }

    public string Description
    {
        get => Draft.Description;
        set => Say(() => Draft.Description = value, nameof(Description));
    }

    public bool AddsBot
    {
        get => Draft.AddsBot;
        set => Say(() => Draft.AddsBot = value, nameof(AddsBot));
    }

    public string BotName
    {
        get => Draft.BotName;
        set => Say(() => Draft.BotName = value, nameof(BotName));
    }

    public string BotDescription
    {
        get => Draft.BotDescription;
        set => Say(() => Draft.BotDescription = value, nameof(BotDescription));
    }

    public string Instructions
    {
        get => Draft.Instructions;
        set => Say(() => Draft.Instructions = value, nameof(Instructions));
    }

    public string SuggestedModel
    {
        get => Draft.SuggestedModel;
        set => Say(() => Draft.SuggestedModel = value, nameof(SuggestedModel));
    }

    public bool NamesServer
    {
        get => Draft.NamesServer;
        set => Say(() => Draft.NamesServer = value, nameof(NamesServer));
    }

    public string ServerName
    {
        get => Draft.ServerName;
        set => Say(() => Draft.ServerName = value, nameof(ServerName));
    }

    public string ServerAddress
    {
        get => Draft.ServerAddress;
        set => Say(() => Draft.ServerAddress = value, nameof(ServerAddress));
    }

    /// <summary>How Drover signs in to this server: <c>none</c>, <c>header</c>, or <c>oauth</c>.</summary>
    public string ServerAuth
    {
        get => Draft.ServerAuth;
        set => Say(() => Draft.ServerAuth = value, nameof(ServerAuth), nameof(UsesHeader));
    }

    public bool UsesHeader => Draft.ServerAuth is "header";

    public string HeaderName
    {
        get => Draft.HeaderName;
        set => Say(() => Draft.HeaderName = value, nameof(HeaderName));
    }

    public string SuggestedTools
    {
        get => Draft.SuggestedTools;
        set => Say(() => Draft.SuggestedTools = value, nameof(SuggestedTools));
    }

    #endregion

    #region Public Methods

    /// <summary>Opens the editor, on the bot a bot's settings named, or on an empty form.</summary>
    public async Task Open(Guid? fromBot)
    {
        botId = fromBot;

        if (await session.Load() is not null)
        {
            await Load();
        }
    }

    [RelayCommand]
    public async Task Load()
    {
        LoadProblem = null;

        if (botId is not { } id || !CanEdit)
        {
            Fill(new());
            return;
        }

        try
        {
            Fill(ExtensionDraft.From(await api.ExportBotAsExtension(SpaceId, id)));
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ExtensionEditorViewModel));
            LoadProblem = Failures.Sentence(exception);
        }
    }

    /// <summary>
    /// Shows the other tab. The form is written out as text at once. Text becomes the form only after the server
    /// has read it: text it cannot read stays on screen, with the reason.
    /// </summary>
    public async Task Show(string chosen)
    {
        Notice = null;

        if (chosen == Tab)
        {
            return;
        }

        if (chosen == JsonTab)
        {
            Text = ExtensionFile.Write(Draft.ToPackage());
            Tab = JsonTab;
            return;
        }

        if (await Read(Text) is { } package)
        {
            Fill(ExtensionDraft.From(package));
            Tab = FormTab;
        }
    }

    [RelayCommand]
    public async Task Check()
    {
        if (await Read(CurrentText) is not null)
        {
            (Notice, NoticeIsProblem) = ("This file reads correctly. It can be imported as it is.", false);
        }
    }

    /// <summary>Puts the file's text on the clipboard with <paramref name="write"/>, which throws when the device refuses.</summary>
    public async Task Copy(Func<string, Task> write)
    {
        try
        {
            await write(CurrentText);
            (Notice, NoticeIsProblem) = ("The file's text is copied.", false);
        }
        catch (DeviceCallFailedException exception)
        {
            logger.LogWarning(exception, "The extension editor could not write to the clipboard");
            (Notice, NoticeIsProblem) = ("The text could not be copied. Open the JSON tab, select the text, and copy it by hand.", true);
        }
    }

    /// <summary>Offers the file to the platform. Cancelling the save picker leaves the editor as it was.</summary>
    public async Task Download(Func<string, string, Task<bool>> save)
    {
        Notice = null;
        try
        {
            if (await save(FileName, CurrentText))
            {
                (Notice, NoticeIsProblem) = ("The file was handed to this device to save.", false);
            }
        }
        catch (DeviceCallFailedException exception)
        {
            logger.LogWarning(exception, "The extension editor could not save the file");
            (Notice, NoticeIsProblem) = ("The file could not be saved. Copy its text from the JSON tab and save it as a JSON file.", true);
        }
    }

    /// <summary>Hands the file to the extensions screen, where it is reviewed like any other file before it is added.</summary>
    [RelayCommand]
    public void Import()
    {
        handoff.Give(CurrentText);
        navigator.NavigateTo(Addresses.Extensions);
    }

    #endregion

    #region Private Methods

    /// <summary>A new form to show: every field is read again.</summary>
    private void Fill(ExtensionDraft filled)
    {
        draft = filled;
        opened = true;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>The package the server reads from the text, or null with the reason shown.</summary>
    private async Task<ExtensionPackage?> Read(string file)
    {
        Busy = true;
        Notice = null;

        try
        {
            return await api.ReadExtension(SpaceId, file);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ExtensionEditorViewModel));
            (Notice, NoticeIsProblem) = (Failures.Sentence(exception, "Check that the server is running."), true);
            return null;
        }
        finally
        {
            Busy = false;
        }
    }

    private void Say(Action change, string property, string? also = null, bool when = true)
    {
        if (!when)
        {
            return;
        }

        change();
        OnPropertyChanged(property);

        if (also is not null)
        {
            OnPropertyChanged(also);
        }
    }

    #endregion
}

/// <summary>What the extension editor shows under its header.</summary>
public enum ExtensionEditorContent
{
    LoadFailed,
    Opening,
    Member,
    Form,
}
