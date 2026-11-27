namespace Drover.Native.Pages;

/// <summary>
/// The extensions of the space. Its job: bring in a file that adds things the space can pick up (a tool server,
/// themes, rules) after reading what it holds, and take one out again.
/// </summary>
public sealed partial class ExtensionsPage : Screen
{
    #region Private Fields

    private readonly Dictionary<Guid, ExtensionRow> rows = [];

    private readonly StackPanel list = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly Sheet importSheet = new() { Title = ExtensionsViewModel.ImportAction };

    private readonly Dialog removeDialog = new();

    private readonly TextBlock removeConsequence = Styles.Text(null);

    private readonly StatusNote removeProblem = new() { Kind = NoteKind.Danger };

    private readonly Button keep = new() { Content = ExtensionsViewModel.KeepLabel };

    private readonly Button remove = new() { Kind = ButtonKind.Danger };

    private ImportForm? form;

    private ExtensionsContent? shownContent;

    private string? shownProblem;

    #endregion

    #region Constructors

    public ExtensionsPage()
    {
        InitializeComponent();
        AutomationProperties.SetName(list, "Extensions");
        ImportExtension.Click += (_, _) => Model.StartImporting();
        keep.Click += (_, _) => Model.KeepIt();
        remove.Click += (_, _) => _ = Model.RemoveCommand.ExecuteAsync(null);
        removeDialog.Body = PageParts.SheetBody(removeConsequence, removeProblem);
        removeDialog.Actions = PageParts.Row(keep, remove);
        removeDialog.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) =>
        {
            if (!removeDialog.Open && Model.Removing is not null)
            {
                Model.KeepIt();
            }
        });
        Layout.Body.Add(removeDialog);
        importSheet.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) =>
        {
            if (!importSheet.Open && Model.Importing)
            {
                Model.StopImporting();
            }
        });
        Layout.Body.Add(importSheet);
        Model.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => ExtensionsViewModel.PageTitle;

    public ExtensionsViewModel Model { get; } = Make<ExtensionsViewModel>();

    #endregion

    #region Private Methods

    protected override Task Opened(RouteMatch<Type> route) => Model.Open();

    private void Refresh()
    {
        ImportExtension.Visibility = Model.ShowsActions ? Visibility.Visible : Visibility.Collapsed;
        ShowImport();
        ShowRemove();
        ShowUnder();

    }

    // The import is made when it begins and let go when it ends: each time it starts it holds a new form.
    private void ShowImport()
    {
        if (Model.Importing && form is null)
        {
            form = new(Model, read => ExtensionReview.Of((ExtensionPreview)read));
            importSheet.Body = form;
        }
        else if (!Model.Importing && form is not null)
        {
            form = null;
            importSheet.Body = null;
        }

        importSheet.Open = Model.Importing;
    }

    // The question names the extension and says what goes with it, so the yes is to something a person has read.
    private void ShowRemove()
    {
        if (Model.Removing is { } going)
        {
            removeDialog.Title = ExtensionWords.RemoveQuestion(going);
            removeConsequence.Text = ExtensionWords.RemoveConsequence(going);
        }

        removeProblem.Text = Model.RemoveProblem;
        remove.Content = Model.RemoveLabel;
        (keep.IsEnabled, remove.IsEnabled) = (!Model.RemoveBusy, !Model.RemoveBusy);
        removeDialog.Open = Model.Removing is not null;
    }

    private void ShowUnder()
    {
        if (Model.Shows == shownContent && Model.LoadProblem == shownProblem)
        {
            if (Model.Shows is ExtensionsContent.List)
            {
                ShowRows();
            }

            return;
        }

        (shownContent, shownProblem) = (Model.Shows, Model.LoadProblem);
        Under.Content = Model.Shows switch
        {
            ExtensionsContent.Loading => Hint(ExtensionsViewModel.LoadingNote, live: true),
            ExtensionsContent.LoadFailed => LoadFailed(),
            ExtensionsContent.Empty => Empty(),
            ExtensionsContent.EmptyForMember => Hint(ExtensionsViewModel.EmptyForMember, live: false),
            ExtensionsContent.List => ListOfRows(),
            _ => null,
        };
    }

    private static TextBlock Hint(string text, bool live)
    {
        var hint = Styles.Text(text, "DvTextSecondary");
        if (live)
        {
            AutomationProperties.SetLiveSetting(hint, AutomationLiveSetting.Polite);
        }

        return hint;
    }

    private EmptyState LoadFailed() => new()
    {
        Title = ExtensionsViewModel.LoadFailedTitle,
        Cause = Model.LoadProblem!,
        ActionLabel = "Try again",
        ActionCommand = Model.LoadCommand,
    };

    private EmptyState Empty() => new()
    {
        Title = ExtensionsViewModel.EmptyTitle,
        Cause = ExtensionsViewModel.EmptyCause,
        ActionLabel = ExtensionsViewModel.ImportAction,
        ActionCommand = Model.StartImportingCommand,
    };

    private StackPanel ListOfRows()
    {
        ShowRows();
        return list;
    }

    // Each extension keeps its row for as long as it is listed, so a value typed into one row survives a change to another.
    private void ShowRows()
    {
        var extensions = Model.Extensions ?? [];
        foreach (var gone in rows.Keys.Where(id => extensions.All(extension => extension.Id != id)).ToList())
        {
            rows.Remove(gone);
        }

        list.Children.Clear();
        foreach (var extension in extensions)
        {
            if (!rows.TryGetValue(extension.Id, out var row))
            {
                row = new(Model);
                row.Changed += Model.Replace;
                rows[extension.Id] = row;
            }

            row.Show(extension);
            list.Children.Add(row);
        }
    }

    #endregion
}
