namespace Drover.Native.Pages;

/// <summary>
/// Importing an extension in two steps: choose the file, then read what it adds before saying yes. The structure
/// is made again when the file has been read or let go; what changes while a step stands (the working state, a
/// sentence of trouble) is updated in place, so nothing typed is lost.
/// </summary>
internal sealed class ImportExtensionForm : Grid
{
    #region Private Fields

    private readonly ExtensionsViewModel model;

    private readonly Facet frame = new() { Role = FacetRole.Frame, Padding = new(Tokens.Number("DvSpace4")) };

    private readonly StackPanel column = new() { Spacing = Tokens.Number("DvSpace4") };

    private readonly TextBlock title = Parts.Heading(null, "DvTextH3", AutomationHeadingLevel.Level2);

    private readonly StackPanel step = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly StatusNote problem = new() { Kind = NoteKind.Danger };

    private readonly Button cancel = new() { Content = ImportExtensionViewModel.CancelLabel };

    private readonly Button read = new() { Kind = ButtonKind.Primary };

    private readonly Button import = new() { Kind = ButtonKind.Primary };

    private readonly TextBlock chosen = Parts.Small(null);

    private TextArea? paste;

    private Button? picker;

    private ExtensionPreview? shownPreview;

    private bool built;

    private FlexPanel? actions;

    #endregion

    #region Constructors

    public ImportExtensionForm(ExtensionsViewModel model)
    {
        this.model = model;
        column.Children.Add(title);
        column.Children.Add(step);
        frame.Children.Add(column);
        Children.Add(frame);

        cancel.Click += (_, _) => model.StopImportingCommand.Execute(null);
        read.Click += (_, _) => _ = model.ReadFormCommand.ExecuteAsync(null);
        import.Click += (_, _) => _ = model.ImportCommand.ExecuteAsync(null);

        var form = model.Form;
        System.ComponentModel.PropertyChangedEventHandler changed = (_, _) => Show();
        model.PropertyChanged += changed;
        form.PropertyChanged += changed;
        Unloaded += (_, _) =>
        {
            model.PropertyChanged -= changed;
            form.PropertyChanged -= changed;
        };
        Loaded += (_, _) => Show();
        this.WhenShownOrRethemed(() =>
        {
            frame.Fill = Tokens.Brush(this, "DvBg1");
            frame.Edge = Tokens.Brush(this, "DvText3");
        });
    }

    #endregion

    #region Private Methods

    private void Show()
    {
        var review = model.Preview is not null;
        title.Text = model.ImportTitle;
        AutomationProperties.SetName(this, model.ImportTitle);

        if (!built || !ReferenceEquals(shownPreview, model.Preview))
        {
            Rebuild();
        }

        problem.Text = review ? model.ImportProblem : model.Form.FileProblem ?? model.ImportProblem;
        cancel.IsEnabled = !model.ImportBusy;
        read.Content = model.ReadLabel;
        read.IsEnabled = !model.ImportBusy && model.Form.CanRead;
        import.Content = model.ImportLabel;
        import.IsEnabled = !model.ImportBusy;

        if (paste is not null)
        {
            paste.Disabled = model.ImportBusy;
            paste.Value = model.Form.Text;
        }

        picker?.IsEnabled = !model.ImportBusy;
    }

    // Only the first step and a change of step are made again. The buttons are kept: they are set free of their
    // old row before they are put in a new one.
    private void Rebuild()
    {
        (built, shownPreview) = (true, model.Preview);
        actions?.Children.Clear();
        step.Children.Clear();
        (paste, picker) = (null, null);

        if (model.Preview is { } preview)
        {
            AddReview(preview);
        }
        else
        {
            AddChoose();
        }
    }

    #endregion

    #region Choose

    private void AddChoose()
    {
        picker = new Button { Content = "Choose a file", HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(picker, ImportExtensionViewModel.FileLabel);
        picker.Click += async (_, _) => await ChooseFile();

        var file = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        var line = new FlexPanel { Gap = Tokens.Number("DvSpace3"), RowGap = Tokens.Number("DvSpace2") };
        (chosen.Parent as Panel)?.Children.Remove(chosen);
        line.Children.Add(picker);
        line.Children.Add(chosen);
        file.Children.Add(Parts.Title(ImportExtensionViewModel.FileLabel, AutomationHeadingLevel.None));
        file.Children.Add(line);

        paste = new TextArea
        {
            Label = ImportExtensionViewModel.PasteLabel,
            Rows = 6,
            Machine = true,
            Hint = ImportExtensionViewModel.PasteHint,
            Value = model.Form.Text,
        };
        paste.RegisterPropertyChangedCallback(TextArea.ValueProperty, (_, _) => model.Form.Text = paste.Value);

        step.Children.Add(file);
        step.Children.Add(paste);
        step.Children.Add(problem);
        step.Children.Add(Actions(cancel, read));
    }

    private async Task ChooseFile()
    {
        try
        {
            var chooser = new Windows.Storage.Pickers.FileOpenPicker();
            chooser.FileTypeFilter.Add(".json");
            if (await chooser.PickSingleFileAsync() is { } file)
            {
                chosen.Text = file.Name;
                var size = (long)(await file.GetBasicPropertiesAsync()).Size;
                await model.Form.Choose(size, () => ReadText(file));
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        {
            Log.For<ImportExtensionForm>().LogWarning(exception, "The platform would not open its file chooser");
            model.Form.FileProblem = "The file could not be read. Choose it again, or paste its text.";
        }
    }

    // A platform that cannot read the file says so in its own words; the form says it in one sentence.
    private static async Task<string> ReadText(Windows.Storage.StorageFile file)
    {
        try
        {
            return await Windows.Storage.FileIO.ReadTextAsync(file);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            throw new IOException("The platform could not read the chosen file.", exception);
        }
    }

    #endregion

    #region Review

    private void AddReview(ExtensionPreview preview)
    {
        if (!string.IsNullOrWhiteSpace(preview.Description))
        {
            step.Children.Add(Parts.Small(preview.Description));
        }

        step.Children.Add(Identity(preview));

        if (preview.AlreadyInstalled)
        {
            step.Children.Add(new StatusNote { Kind = NoteKind.Info, Text = "This extension is already in this space, so it cannot be imported again." });
        }
        else if (preview.Bot is { } bot)
        {
            AddBot(bot);
        }

        if (preview is { AlreadyInstalled: false, Connection: { } connection })
        {
            AddServer(connection);
        }

        step.Children.Add(problem);
        step.Children.Add(model.CanImport ? Actions(cancel, import) : Actions(cancel));
    }

    private static FlexPanel Identity(ExtensionPreview preview)
    {
        var id = new FlexPanel { Gap = Tokens.Number("DvSpace1") };
        id.Children.Add(Parts.Small("Extension id"));
        id.Children.Add(Parts.Machine(preview.PackageId, "DvText12", "DvText3"));
        return id;
    }

    private void AddBot(ExtensionBotPreview bot)
    {
        var facts = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        facts.Children.Add(Parts.Fact("Adds the bot", Parts.Body(bot.Name)));

        if (!string.IsNullOrWhiteSpace(bot.Description))
        {
            facts.Children.Add(Parts.Fact("What it is for", Parts.Body(bot.Description)));
        }

        if (bot.Model is not null)
        {
            facts.Children.Add(Parts.Fact("Thinks with", Parts.Body($"{bot.Model} on {bot.ModelConnection}. You can change this in the bot's settings.")));
        }

        facts.Children.Add(Parts.Fact("Tools", Parts.Body("Only what every new bot starts with. The file cannot give it more.")));
        step.Children.Add(facts);
        step.Children.Add(Instructions(bot));

        if (bot.Model is null)
        {
            var hint = new FlexPanel { Gap = 0 };
            hint.Children.Add(Parts.Small("A bot needs a model to think with, and this space has none it can use yet."));
            hint.Children.Add(Links.Plain("Connect a model", Addresses.Models));
            hint.Children.Add(Parts.Small(", then import this."));
            step.Children.Add(hint);
        }
    }

    private static StackPanel Instructions(ExtensionBotPreview bot)
    {
        var shown = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        const string Heading = "What the bot will be told, in full";
        shown.Children.Add(Parts.Title(Heading, AutomationHeadingLevel.Level3));

        if (bot.HiddenCharacters > 0)
        {
            shown.Children.Add(new StatusNote { Kind = NoteKind.Danger, Text = ExtensionWords.Hidden(bot.HiddenCharacters) });
        }

        if (string.IsNullOrEmpty(bot.Instructions))
        {
            shown.Children.Add(Parts.Small("The file gives this bot no instructions."));
            return shown;
        }

        // The instructions are shown whole: the frame grows with them and long lines wrap, so nothing is off the screen.
        var text = Parts.Machine(bot.Instructions, "DvText13", "DvText");
        text.IsTextSelectionEnabled = true;
        AutomationProperties.SetName(text, Heading);
        var box = new Border { Child = text, Padding = new(Tokens.Number("DvSpace3")), BorderThickness = new(Tokens.Number("DvBorder")) };
        box.Loaded += (_, _) => (box.Background, box.BorderBrush) = (Tokens.Brush(box, "DvBg0"), Tokens.Brush(box, "DvLine"));
        box.ActualThemeChanged += (_, _) => (box.Background, box.BorderBrush) = (Tokens.Brush(box, "DvBg0"), Tokens.Brush(box, "DvLine"));
        shown.Children.Add(box);
        return shown;
    }

    private void AddServer(ExtensionConnectionPreview connection)
    {
        var server = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        var rule = new Border { Child = server, Padding = new(0, Tokens.Number("DvSpace3"), 0, 0), BorderThickness = new(0, Tokens.Number("DvBorder"), 0, 0) };
        rule.Loaded += (_, _) => rule.BorderBrush = Tokens.Brush(rule, "DvLine");
        rule.ActualThemeChanged += (_, _) => rule.BorderBrush = Tokens.Brush(rule, "DvLine");

        server.Children.Add(Parts.Title("Names a tool server", AutomationHeadingLevel.Level3));
        server.Children.Add(Parts.Machine(connection.Host, "DvText20", "DvText"));

        var facts = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        facts.Children.Add(Parts.Fact("Called", Parts.Body(connection.Name)));
        facts.Children.Add(Parts.Fact("Full address", Parts.Machine(connection.Url, "DvText12", "DvText3")));
        facts.Children.Add(Parts.Fact("How Drover proves who is calling", Parts.Body(ExtensionWords.Auth(connection.Auth, connection.HeaderName))));

        if (connection.SuggestedTools.Count > 0)
        {
            facts.Children.Add(Parts.Fact("Tools it suggests for the bot", SuggestedTools(connection)));
        }

        server.Children.Add(facts);

        if (!connection.Available)
        {
            server.Children.Add(new StatusNote
            {
                Kind = NoteKind.Info,
                Text = "Tool connections are turned off on this server, so the tool server is not connected. Everything else in the file is added.",
            });
        }
        else
        {
            server.Children.Add(Parts.Small("Importing contacts nothing. You connect the server afterwards, and then choose which tools the bot gets."));

            if (connection.IsLocal)
            {
                server.Children.Add(new StatusNote
                {
                    Kind = NoteKind.Info,
                    Text = "This address is on this server's own network, or is not https. You are asked again before it is connected.",
                });
            }
        }

        step.Children.Add(rule);
    }

    private static StackPanel SuggestedTools(ExtensionConnectionPreview connection)
    {
        var tools = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
        foreach (var tool in connection.SuggestedTools)
        {
            var line = new Grid();
            line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var why = Parts.Body(string.IsNullOrWhiteSpace(tool.Why) ? null : $": {tool.Why}");
            Grid.SetColumn(why, 1);
            line.Children.Add(Parts.Machine(tool.Name, "DvText12", "DvText3"));
            line.Children.Add(why);
            tools.Children.Add(line);
        }

        return tools;
    }

    #endregion

    #region Layout

    private FlexPanel Actions(params Button[] buttons)
    {
        actions ??= new FlexPanel { Gap = Tokens.Number("DvSpace3"), RowGap = Tokens.Number("DvSpace2"), Justify = FlexJustify.End };
        actions.Children.Clear();
        foreach (var button in buttons)
        {
            actions.Children.Add(button);
        }

        return actions;
    }

    #endregion
}
