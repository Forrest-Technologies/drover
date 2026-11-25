namespace Drover.Native.Pages;

/// <summary>
/// The extensions of the space. Its job: bring in a bot someone shared, after reading exactly what it will be told,
/// and turn it on.
/// </summary>
public sealed partial class ExtensionsPage : Screen
{
    #region Private Fields

    private readonly Dictionary<Guid, ExtensionRow> rows = [];

    private readonly Navigator navigator = ((App)Application.Current).Services.GetRequiredService<Navigator>();

    private readonly StackPanel list = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly Sheet importSheet = new() { Title = "Import an extension" };

    private ImportExtensionForm? form;

    private ExtensionsContent? shownContent;

    private string? shownProblem;

    #endregion

    #region Constructors

    public ExtensionsPage()
    {
        InitializeComponent();
        AutomationProperties.SetName(list, "Extensions");
        AddExtension.Items.Add(PageParts.MenuEntry("Import an extension", () => { Model.StartImporting(); Refresh(); }));
        AddExtension.Items.Add(PageParts.MenuEntry("Make an extension", () => navigator.NavigateTo(Addresses.ExtensionEditor)));
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
        AddExtension.Visibility = Model.ShowsActions ? Visibility.Visible : Visibility.Collapsed;
        ShowImport();
        ShowUnder();

        foreach (var row in rows.Values)
        {
            row.Refresh();
        }
    }

    // The import is made when it begins and let go when it ends: each time it starts it holds a new form.
    private void ShowImport()
    {
        if (Model.Importing && form is null)
        {
            form = new(Model);
            importSheet.Body = form;
        }
        else if (!Model.Importing && form is not null)
        {
            form = null;
            importSheet.Body = null;
        }

        importSheet.Open = Model.Importing;
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
        ActionLabel = "Import an extension",
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
