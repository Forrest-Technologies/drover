namespace Drover.Native.Pages;

/// <summary>
/// Importing a file in two steps: choose the file, then read what it adds before saying yes. The bots screen and
/// the extensions screen each show one, with their own review. The structure is made again when the file has been
/// read or let go; what changes while a step stands (the working state, a sentence of trouble) is updated in place,
/// so nothing typed is lost.
/// </summary>
internal sealed class ImportForm : Grid
{
    #region Private Fields

    private readonly IImportFlow model;

    private readonly Func<object, IEnumerable<UIElement>> review;

    private readonly Facet frame = new() { Role = FacetRole.Frame, Padding = new(Tokens.Number("DvSpace4")) };

    private readonly StackPanel column = new() { Spacing = Tokens.Number("DvSpace4") };

    private readonly TextBlock title = Parts.Heading(null, "DvTextH3", AutomationHeadingLevel.Level2);

    private readonly StackPanel step = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly StatusNote problem = new() { Kind = NoteKind.Danger };

    private readonly Button cancel = new() { Content = ImportFileViewModel.CancelLabel };

    private readonly Button read = new() { Kind = ButtonKind.Primary };

    private readonly Button import = new() { Kind = ButtonKind.Primary };

    private readonly TextBlock chosen = Parts.Small(null);

    private TextArea? paste;

    private Button? picker;

    private object? shownReview;

    private bool built;

    private FlexPanel? actions;

    #endregion

    #region Constructors

    /// <param name="model">The import the form shows.</param>
    /// <param name="review">What the second step shows of the file the server read: the parts a person reads before saying yes.</param>
    public ImportForm(IImportFlow model, Func<object, IEnumerable<UIElement>> review)
    {
        this.model = model;
        this.review = review;
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
        var reviewing = model.Reviewed is not null;
        title.Text = model.ImportTitle;
        AutomationProperties.SetName(this, model.ImportTitle);

        if (!built || !ReferenceEquals(shownReview, model.Reviewed))
        {
            Rebuild();
        }

        problem.Text = reviewing ? model.ImportProblem : model.Form.FileProblem ?? model.ImportProblem;
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
        (built, shownReview) = (true, model.Reviewed);
        actions?.Children.Clear();
        step.Children.Clear();
        (paste, picker) = (null, null);

        if (model.Reviewed is { } read)
        {
            foreach (var part in review(read))
            {
                step.Children.Add(part);
            }

            step.Children.Add(problem);
            step.Children.Add(model.CanImport ? Actions(cancel, import) : Actions(cancel));
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
        AutomationProperties.SetName(picker, model.Form.FileLabel);
        picker.Click += async (_, _) => await ChooseFile();

        var file = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        var line = new FlexPanel { Gap = Tokens.Number("DvSpace3"), RowGap = Tokens.Number("DvSpace2") };
        (chosen.Parent as Panel)?.Children.Remove(chosen);
        line.Children.Add(picker);
        line.Children.Add(chosen);
        file.Children.Add(Parts.Title(model.Form.FileLabel, AutomationHeadingLevel.None));
        file.Children.Add(line);

        paste = new TextArea
        {
            Label = ImportFileViewModel.PasteLabel,
            Rows = 6,
            Machine = true,
            Hint = ImportFileViewModel.PasteHint,
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
            Log.For<ImportForm>().LogWarning(exception, "The platform would not open its file chooser");
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
