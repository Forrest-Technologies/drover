namespace Drover.Native.Pages;

/// <summary>The Model section of the bot editor: which model the bot thinks with, and only the settings that model has.</summary>
public sealed class ModelSectionView : EditorPart
{
    #region Private Fields

    private readonly Select connection;

    private readonly TextBlock loading = BotParts.Hint(ModelSectionViewModel.LoadingModels);

    private readonly Select model;

    private readonly Field modelName;

    private readonly Button entryToggle;

    private readonly TextBlock unknown = BotParts.Hint(ModelSectionViewModel.UnknownModel);

    private readonly TextBlock readersNotKnown = BotParts.Hint(ModelSectionViewModel.ReadersNotKnown);

    private readonly TextBlock noReaders = BotParts.Hint(ModelSectionViewModel.NoReaders);

    private readonly Select reader;

    private readonly TextBlock readerHint = BotParts.Hint();

    private readonly StackPanel needsScreen;

    private readonly Select reasoning;

    private readonly StackPanel webSearchChoice;

    private readonly SegmentedControl webSearch;

    private readonly Switch structured;

    private readonly TextBlock withoutTools = BotParts.Hint(ModelSectionViewModel.AppliesWithoutTools);

    private readonly Field temperature;

    private readonly Field maxOutput;

    #endregion

    #region Constructors

    public ModelSectionView(BotEditorViewModel editor)
        : base(editor)
    {
        var part = editor.ModelPart;
        AutomationProperties.SetLiveSetting(loading, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(readersNotKnown, AutomationLiveSetting.Polite);
        connection = new Select { Label = ModelSectionViewModel.ConnectionLabel }.OnChanged(Quiet, async value => await part.ChooseConnection(value));
        model = new Select { Label = ModelSectionViewModel.ModelLabel }.OnChanged(Quiet, part.ChooseModel);
        modelName = new Field { Label = ModelSectionViewModel.ModelNameLabel }.OnChanged(Quiet, part.ChooseModel);
        entryToggle = BotParts.Action(string.Empty, part.ToggleModelEntry);
        reader = new Select { Label = ModelSectionViewModel.ReaderLabel }.OnChanged(Quiet, part.ChooseReader);
        needsScreen = BotParts.Column(
            "DvSpace2",
            BotParts.Hint(ModelSectionViewModel.NeedsScreen),
            BotParts.Action(ModelSectionViewModel.OpenSandbox, () => Editor.Toggle(BotEditorViewModel.SandboxSection, true)));
        reasoning = new Select { Label = ModelSectionViewModel.ReasoningLabel }.OnChanged(Quiet, value => Editor.Edit(draft => draft.ReasoningEffort = value));
        webSearch = new SegmentedControl { Label = ModelSectionViewModel.WebSearchLabel }.OnChanged(Quiet, value => Editor.Edit(draft => draft.WebSearch = value));
        webSearchChoice = BotParts.Column("DvSpace1", BotParts.ChoiceLabel(ModelSectionViewModel.WebSearchLabel), webSearch);
        structured = new Switch { Label = ModelSectionViewModel.StructuredOutputLabel }.OnChanged(Quiet, on => Editor.Edit(draft => draft.StructuredOutput = on));
        temperature = DraftField(
            ModelSectionViewModel.TemperatureLabel, (draft, value) => draft.Temperature = value, ModelSectionViewModel.LeftToModel, ModelSectionViewModel.TemperatureHint);
        maxOutput = DraftField(ModelSectionViewModel.MaxOutputLabel, (draft, value) => draft.MaxOutputTokens = value, ModelSectionViewModel.LeftToModel);
        var manage = new Link { Text = ModelSectionViewModel.ManageConnections, Address = ModelSectionViewModel.ManageConnectionsAddress, FontSize = Tokens.Number("DvText13") };
        var pair = new PairPanel();
        pair.Children.Add(temperature);
        pair.Children.Add(maxOutput);

        foreach (var child in new UIElement[]
        {
            connection, loading, model, modelName, entryToggle, manage, unknown, readersNotKnown, noReaders, reader, readerHint, needsScreen, reasoning,
            webSearchChoice, structured, withoutTools, pair,
        })
        {
            Parts.Add(child);
        }
    }

    #endregion

    #region Private Methods

    protected override void Update()
    {
        var part = Editor.ModelPart;
        var loaded = !part.LoadingShown;
        connection.Show(part.ConnectionOptions, part.ConnectionValue);
        loading.Shown(!loaded);
        model.Shown(loaded && part.ModelChosenFromList);
        modelName.Shown(loaded && !part.ModelChosenFromList);

        if (loaded && part.ModelChosenFromList)
        {
            model.Show(part.ModelOptions, Draft.Model);
        }

        modelName.Hint = part.ModelFieldHint;
        modelName.Show(Draft.Model);
        entryToggle.Shown(part.EntryToggleShown).Content = part.EntryToggleLabel;
        unknown.Shown(part.UnknownModelShown);
        ShowReader(part);
        reasoning.Shown(part.ReasoningOptions is not null);

        if (part.ReasoningOptions is { } efforts)
        {
            reasoning.Show(efforts, Draft.ReasoningEffort);
        }

        webSearchChoice.Shown(part.WebSearchShown);
        webSearch.Show(ModelSectionViewModel.WebSearchOptions, Draft.WebSearch);
        structured.Shown(part.StructuredOutputShown).Show(Draft.StructuredOutput);
        withoutTools.Shown(part.StructuredOutputShown && part.AppliesWithoutToolsShown);
        temperature.Show(Draft.Temperature);
        maxOutput.Hint = part.MaxOutputHint;
        maxOutput.Show(Draft.MaxOutputTokens);
    }

    // A model that cannot see images uses a screen through another model that reads it, with the centre of each thing to click.
    private void ShowReader(ModelSectionViewModel part)
    {
        var shows = part.ReaderShown ? part.ReaderShows : (ReaderShows?)null;
        readersNotKnown.Shown(shows is ReaderShows.NotKnown);
        noReaders.Shown(shows is ReaderShows.NoneToChoose);
        reader.Shown(shows is ReaderShows.Choice);
        readerHint.Shown(shows is ReaderShows.Choice);
        needsScreen.Shown(shows is ReaderShows.Choice && part.NeedsScreenShown);

        if (shows is ReaderShows.Choice)
        {
            reader.Show(part.ReaderOptions, part.ReaderValue);
            readerHint.Show(part.ReaderHint);
        }
    }

    #endregion
}
