namespace Drover.Presentation.ViewModels;

/// <summary>The Model section of the bot editor: which model the bot thinks with, and only the settings that model has.</summary>
public sealed class ModelSectionViewModel(BotEditorViewModel editor)
{
    #region Private Fields

    private static readonly SegmentOption<string>[] webSearch = [new("off", "Off"), new("automatic", "When needed"), new("on", "Always")];

    private bool enterModelId;

    #endregion

    #region Properties

    public static string ConnectionLabel => "Connection";

    public static string ModelLabel => "Model";

    public static string ModelNameLabel => "Model name";

    public static string LoadingModels => "Loading the models of this connection.";

    public static string ManageConnections => "Manage model connections";

    public static string ManageConnectionsAddress => Addresses.Models;

    public static string UnknownModel => "Drover does not know what this model can do, so only the settings every model has are shown.";

    public static string ReadersNotKnown => "The models that can read the screen are not known yet.";

    public static string NoReaders =>
        "This model cannot see images, and no model of this space is listed as reading them, so no other model can read the screen for this bot.";

    public static string ReaderLabel => "Reads the screen for this bot";

    public static string NeedsScreen => "It is used when this bot has a screen. Give it a sandbox with Desktop on, in the Sandbox section.";

    public static string OpenSandbox => "Open the Sandbox section";

    public static string ReasoningLabel => "Reasoning effort";

    public static string WebSearchLabel => "Built-in web search";

    public static IReadOnlyList<SegmentOption<string>> WebSearchOptions => webSearch;

    public static string StructuredOutputLabel => "Answer as a JSON object";

    public static string AppliesWithoutTools => "Applies when tools are unavailable.";

    public static string TemperatureLabel => "Temperature";

    public static string TemperatureHint => "0 is steady, 2 is loose.";

    public static string MaxOutputLabel => "Longest answer, in tokens";

    public static string LeftToModel => "Left to the model";

    public IReadOnlyList<ChoiceOption> ConnectionOptions =>
        [.. editor.Connections.Select(connection => new ChoiceOption(connection.Id.ToString(), connection.Name))];

    public string ConnectionValue => Draft.ModelConnectionId.ToString();

    /// <summary>True while the models of the chosen connection are being read.</summary>
    public bool LoadingShown => editor.Models is null;

    /// <summary>The model is chosen from the catalog, or else typed in a field.</summary>
    public bool ModelChosenFromList => Choices.Count > 0 && !enterModelId;

    public IReadOnlyList<ChoiceOption> ModelOptions =>
    [
        .. Choices.Any(entry => entry.Model == Draft.Model) ? [] : (ChoiceOption[])[new(Draft.Model, Draft.Model)],
        .. Choices.Select(entry => new ChoiceOption(entry.Model, entry.DisplayName)),
    ];

    public string ModelFieldHint =>
        enterModelId ? "Enter the model ID your provider expects." : "This connection does not list its models. Type the name it expects.";

    public bool EntryToggleShown => editor.Models is not null && Choices.Count > 0;

    public string EntryToggleLabel => enterModelId ? "Choose from the catalog" : "Enter a model ID";

    public bool UnknownModelShown => Known is null && editor.Models is not null;

    // Offered wherever it can matter: a reader is set, or the model cannot see and there is a model that can, or the
    // bot has a screen it cannot see. A bot that sees for itself and has none set is not asked.
    public bool ReaderShown =>
        Draft.ScreenReader is not null
        || (editor.ModelSeesImages is false && (Draft.Desktop || editor.Readers is { Count: > 0 }));

    /// <summary>Which of the three things the reader part shows, while it is shown.</summary>
    public ReaderShows ReaderShows => editor.Readers switch
    {
        null => ReaderShows.NotKnown,
        { Count: 0 } when Draft.ScreenReader is null => ReaderShows.NoneToChoose,
        _ => ReaderShows.Choice,
    };

    public IReadOnlyList<ChoiceOption> ReaderOptions =>
    [
        new(string.Empty, OwnModel),
        .. Draft.ScreenReader is { } stored && Chosen is null
            ? (ChoiceOption[])[new(ScreenReaderChoice.ValueOf(stored.ModelConnectionId, stored.Model), stored.Model)]
            : [],
        .. (editor.Readers ?? []).Select(reader => new ChoiceOption(reader.Value, reader.DisplayName, reader.ConnectionName)),
    ];

    public string ReaderValue => Draft.ScreenReader is { } reader ? ScreenReaderChoice.ValueOf(reader.ModelConnectionId, reader.Model) : string.Empty;

    public string ReaderHint => Draft.ScreenReader is null
        ? "This model cannot see images. Choose a model that can, and it reads each screenshot for the bot: what is on the screen, and where to click."
        : $"The bot gets a description of each screenshot in words instead of the picture. That is slower and less exact than a model that sees for itself, and each screenshot is also a call to {Chosen?.DisplayName ?? Draft.ScreenReader.Model}.";

    public bool NeedsScreenShown => !Draft.Desktop;

    /// <summary>The efforts the model offers, with the choice of leaving it to the model first, or null when it offers none.</summary>
    public IReadOnlyList<ChoiceOption>? ReasoningOptions => Known is { ReasoningEfforts.Count: > 0 } reasoning
        ? [new(string.Empty, LeftToModel), .. reasoning.ReasoningEfforts.Select(effort => new ChoiceOption(effort, effort))]
        : null;

    public bool WebSearchShown => Known is { WebSearch: true };

    public bool StructuredOutputShown => Known is { StructuredOutput: true };

    // Said for any model that reports structured output, whichever provider it comes from: the model's own
    // capabilities decide, not the name of the kind of its connection.
    public bool AppliesWithoutToolsShown => Draft.StructuredOutput;

    public string? MaxOutputHint => Known?.MaxOutputSize is { } most ? $"This model writes at most {most}." : null;

    public ModelCapabilitiesResponse? Known => editor.Models?.FirstOrDefault(entry => entry.Model == Draft.Model)?.Capabilities;

    private BotDraft Draft => editor.Draft!;

    private IReadOnlyList<ModelCatalogResponse> Choices =>
        [.. (editor.Models ?? []).Where(entry => entry.State is "available").OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)];

    private ScreenReaderChoice? Chosen =>
        Draft.ScreenReader is { } reader
            ? editor.Readers?.FirstOrDefault(choice => choice.ConnectionId == reader.ModelConnectionId && choice.Model == reader.Model)
            : null;

    private string OwnModel => editor.ModelSeesImages is false ? "Its own model (cannot read images)" : "Its own model";

    #endregion

    #region Public Methods

    public async Task ChooseConnection(string id)
    {
        if (Guid.TryParse(id, out var connectionId) && connectionId != Draft.ModelConnectionId)
        {
            enterModelId = false;
            Draft.ModelConnectionId = connectionId;
            await editor.ChooseConnection(connectionId);
        }
    }

    public void ChooseModel(string model)
    {
        Draft.Model = model;
        Draft.KeepWhatModelSupports(Known);
        editor.Changed();
    }

    public void ChooseReader(string value)
    {
        Draft.ScreenReader = ScreenReaderChoice.Read(value);
        editor.Changed();
    }

    /// <summary>The section was closed: it opens on the catalog again, as on the web.</summary>
    public void Reset() => enterModelId = false;

    public void ToggleModelEntry()
    {
        enterModelId = !enterModelId;
        editor.Notify();
    }

    #endregion
}
