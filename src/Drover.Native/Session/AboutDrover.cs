namespace Drover.Native.Session;

/// <summary>
/// What the app is, as the account menu's About dialog shows it: the version and platform a bug report asks for, and
/// saving the diagnostic log to attach to one.
/// </summary>
public sealed class AboutDrover : StackPanel
{
    #region Private Fields

    private readonly AboutViewModel model =
        ActivatorUtilities.CreateInstance<AboutViewModel>(((App)Application.Current).Services);

    private readonly StatusNote notice = new();

    #endregion

    #region Constructors

    public AboutDrover()
    {
        Spacing = Tokens.Number("DvSpace3");
        AutomationProperties.SetLiveSetting(notice, AutomationLiveSetting.Polite);

        Children.Add(Fact("Version", model.Version));
        Children.Add(Fact("Platform", model.Platform));
        Children.Add(Styles.Text(AboutViewModel.Kept, "DvTextSecondary"));
        Children.Add(SaveButton());
        Children.Add(notice);

        model.PropertyChanged += (_, _) => ShowNotice();
    }

    #endregion

    #region Private Methods

    private static StackPanel Fact(string label, string value)
    {
        var fact = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
        fact.Children.Add(Styles.Text(label, "DvTextLabel"));
        var text = Styles.Text(value, "DvTextMono");
        text.TextWrapping = TextWrapping.Wrap;
        fact.Children.Add(text);
        return fact;
    }

    private Button SaveButton()
    {
        var save = new Button
        {
            Kind = ButtonKind.Primary,
            Content = "Save diagnostics",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            await model.SaveDiagnostics(PlatformFiles.SaveLog);
            save.IsEnabled = true;
        };
        return save;
    }

    private void ShowNotice()
    {
        notice.Kind = model.NoticeIsProblem ? NoteKind.Danger : NoteKind.Ok;
        notice.Text = model.Notice;
    }

    #endregion
}
