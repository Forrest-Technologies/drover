namespace Drover.Ui.Pages.Extensions;

/// <summary>
/// The extension editor. Its job: write a bot down so someone else can import it, and hand over the file. The form
/// comes first; the same file as text is one tab away, and the server reads that text whenever it becomes the form.
/// </summary>
public sealed partial class ExtensionEditor(
    DroverApi api,
    SessionState session,
    ExtensionHandoff handoff,
    NavigationManager navigation,
    IJSRuntime js,
    IServiceProvider services,
    ILogger<ExtensionEditor> logger) : IAsyncDisposable
{
    #region Private Fields

    private const string FormTab = "form";

    private const string JsonTab = "json";

    private const string ClipboardModule = "./_content/Drover.Ui/Components/CodeBlock.razor.js";

    private static readonly TabItem[] tabs = [new(FormTab, "Form"), new(JsonTab, "JSON")];

    private IJSObjectReference? clipboard;

    private ExtensionDraft? draft;

    private string tab = FormTab;

    private string text = string.Empty;

    private string? loadProblem;

    private string? notice;

    private bool noticeIsProblem;

    private bool busy;

    #endregion

    #region Properties

    /// <summary>The bot to start from, when the editor was opened from a bot's settings.</summary>
    [SupplyParameterFromQuery(Name = "bot")]
    public Guid? BotId { get; set; }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    // The server decides this for every request. Here it only keeps controls a person cannot use off the screen.
    private bool CanEdit => session.Space?.Role is "owner" or "admin";

    /// <summary>A browser saves a link's file itself. The native app has no such link, so there the text is copied or imported.</summary>
    private bool CanDownload => services.GetService<IFileDelivery>() is null;

    /// <summary>The file as it stands now: the text being edited, or the form written out.</summary>
    private string CurrentText => tab == JsonTab ? text : ExtensionFile.Write(draft!.ToPackage());

    private string FileName => $"{(string.IsNullOrWhiteSpace(draft?.Id) ? "extension" : draft.Id.Trim())}.json";

    private string DownloadAddress => $"data:application/json;charset=utf-8,{Uri.EscapeDataString(CurrentText)}";

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        if (clipboard is not null)
        {
            try
            {
                await clipboard.DisposeAsync();
            }
            catch (JSDisconnectedException gone)
            {
                logger.LogDebug(gone, "The page went away before the extension editor's clipboard module was released");
            }
        }
    }

    #endregion

    #region Overrides

    protected override async Task OnInitializedAsync()
    {
        if (await session.Load() is not null)
        {
            await Load();
        }
    }

    #endregion

    #region Private Methods

    private async Task Load()
    {
        loadProblem = null;

        if (BotId is not { } botId || !CanEdit)
        {
            draft = new();
            return;
        }

        try
        {
            draft = ExtensionDraft.From(await api.ExportBotAsExtension(SpaceId, botId));
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ExtensionEditor));
            loadProblem = Failures.Sentence(exception);
        }
    }

    private void Rename(string name) => draft!.Rename(name);

    private void ChooseId(string id) => draft!.ChooseId(id);

    private void EditText(string edited) => text = edited;

    /// <summary>
    /// Shows the other tab. The form is written out as text at once. Text becomes the form only after the server
    /// has read it: text it cannot read stays on screen, with the reason.
    /// </summary>
    private async Task Show(string chosen)
    {
        notice = null;

        if (chosen == tab)
        {
            return;
        }

        if (chosen == JsonTab)
        {
            text = ExtensionFile.Write(draft!.ToPackage());
            tab = JsonTab;
            return;
        }

        if (await Read(text) is { } package)
        {
            draft = ExtensionDraft.From(package);
            tab = FormTab;
        }
    }

    private async Task Check()
    {
        if (await Read(CurrentText) is not null)
        {
            (notice, noticeIsProblem) = ("This file reads correctly. It can be imported as it is.", false);
        }
    }

    /// <summary>The package the server reads from the text, or null with the reason shown.</summary>
    private async Task<ExtensionPackage?> Read(string file)
    {
        busy = true;
        notice = null;

        try
        {
            return await api.ReadExtension(SpaceId, file);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ExtensionEditor));
            (notice, noticeIsProblem) = (Failures.Sentence(exception), true);
            return null;
        }
        finally
        {
            busy = false;
        }
    }

    private async Task Copy()
    {
        try
        {
            clipboard ??= await js.InvokeAsync<IJSObjectReference>("import", ClipboardModule);
            await clipboard.InvokeVoidAsync("copyText", CurrentText);
            (notice, noticeIsProblem) = ("The file's text is copied.", false);
        }
        catch (JSException exception)
        {
            logger.LogWarning(exception, "The extension editor could not write to the clipboard");
            (notice, noticeIsProblem) = ("The text could not be copied. Open the JSON tab, select the text, and copy it by hand.", true);
        }
    }

    /// <summary>Hands the file to the extensions screen, where it is reviewed like any other file before it is added.</summary>
    private void Import()
    {
        handoff.Give(CurrentText);
        navigation.NavigateTo(Addresses.Extensions);
    }

    #endregion
}
