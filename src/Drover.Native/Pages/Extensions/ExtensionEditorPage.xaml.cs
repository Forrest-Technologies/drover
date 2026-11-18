namespace Drover.Native.Pages;

/// <summary>
/// The extension editor. Its job: write a bot down so someone else can import it, and hand over the file. The form
/// comes first; the same file as text is one tab away, and the server reads that text whenever it becomes the form.
/// </summary>
public sealed partial class ExtensionEditorPage : Screen
{
    #region Constructors

    public ExtensionEditorPage()
    {
        InitializeComponent();
        Back.Address = Addresses.Extensions;
        Back.Ink = Back.HoverInk = "DvText";
        Back.MinHeight = Tokens.Number("DvTargetTouch");
        Back.VerticalContentAlignment = VerticalAlignment.Center;
        Editor.Padding = new(Tokens.Number("DvSpace4"));
        Copy.Click += async (_, _) => await Model.Copy(WriteToClipboard);
        Download.Click += async (_, _) => await Model.Download(PlatformFiles.SaveText);
        HowShown.RegisterPropertyChangedCallback(Tabs.ActiveProperty, (_, _) => _ = ShowTab());
        Model.PropertyChanged += (_, change) => Refresh(change.PropertyName);
        Refresh(string.Empty);
    }

    #endregion

    #region Properties

    public override string Title => ExtensionEditorViewModel.PageTitle;

    public ExtensionEditorViewModel Model { get; } = Make<ExtensionEditorViewModel>();

    #endregion

    #region Private Methods

    protected override async void Opened(RouteMatch<Type> route) =>
        await Model.Open(Guid.TryParse(route.QueryValue("bot"), out var bot) ? bot : null);

    // A form read again from text changes every field at once: the bindings are told to read them all.
    private void Refresh(string? changed)
    {
        Download.Content = $"Download {Model.FileName}";
        if (string.IsNullOrEmpty(changed))
        {
            Bindings.Update();
        }

        BotFields.Visibility = Model.AddsBot ? Visibility.Visible : Visibility.Collapsed;
        ServerFields.Visibility = Model.NamesServer ? Visibility.Visible : Visibility.Collapsed;
        HeaderField.Visibility = Model.UsesHeader ? Visibility.Visible : Visibility.Collapsed;
        AsText.Visibility = Model.ShowsJson ? Visibility.Visible : Visibility.Collapsed;
        FormFields.Visibility = Model.ShowsJson ? Visibility.Collapsed : Visibility.Visible;
        Editor.Visibility = Model.Shows is ExtensionEditorContent.Form ? Visibility.Visible : Visibility.Collapsed;
        Under.Content = Model.Shows switch
        {
            ExtensionEditorContent.LoadFailed => LoadFailed(),
            ExtensionEditorContent.Opening => Hint(ExtensionEditorViewModel.OpeningNote, live: true),
            ExtensionEditorContent.Member => Hint(ExtensionEditorViewModel.MemberNote, live: false),
            _ => null,
        };

        if (HowShown.Active != Model.Tab)
        {
            HowShown.Active = Model.Tab;
        }
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
        Title = ExtensionEditorViewModel.LoadFailedTitle,
        Cause = Model.LoadProblem!,
        ActionLabel = "Try again",
        ActionCommand = Model.LoadCommand,
    };

    // The tab shows what the model says it shows: a text the server cannot read keeps the tab it is on.
    private async Task ShowTab()
    {
        var chosen = HowShown.Active;
        if (chosen is null || chosen == Model.Tab)
        {
            return;
        }

        HowShown.Active = Model.Tab;
        await Model.Show(chosen);
    }

    private static Task WriteToClipboard(string text)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            return Task.CompletedTask;
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new DeviceCallFailedException("The platform refused to copy the file's text.", exception);
        }
    }

    #endregion
}
