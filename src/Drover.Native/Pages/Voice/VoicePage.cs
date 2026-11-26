namespace Drover.Native.Pages;

/// <summary>Choose which model listens, which speaks and which holds live calls, and hear a voice before choosing it.</summary>
public sealed class VoicePage : Screen
{
    #region Private Fields

    private readonly WorkLayout layout = new();

    private readonly VoiceSettingsViewModel model = Make<VoiceSettingsViewModel>();

    private readonly StatusNote deviceNote = new();

    private readonly StatusNote saveProblem = new() { Kind = NoteKind.Danger };

    // What stands in for the rows: loading, a failed load, or nothing connected.
    private readonly ContentPresenter notice = new();

    private readonly StackPanel rows = new() { Spacing = Tokens.Number("DvSpace4") };

    private readonly TextBlock membersRead = Styles.Text(VoiceWords.MembersRead, "DvTextSecondary");

    private readonly Disclosure advanced = new() { Title = VoiceWords.Advanced };

    // A row is kept while the page is told of a change, so the picker a person just used keeps the focus.
    private readonly Dictionary<VoiceSlot, VoiceRowView> shown = [];

    private string? noticeShown;

    #endregion

    #region Constructors

    public VoicePage()
    {
        Content = layout;
        layout.Body.Add(new ScreenHeader { Title = VoiceWords.PageTitle });
        layout.Body.Add(deviceNote);
        layout.Body.Add(notice);
        layout.Body.Add(rows);
        layout.Body.Add(saveProblem);
        layout.Body.Add(membersRead);
        layout.Body.Add(advanced);
        model.PropertyChanged += (_, _) => Refresh();
        Unloaded += (_, _) => model.Leave();
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => "Voice · Drover";

    #endregion

    #region Private Methods

    protected override Task Opened(RouteMatch<Type> route) => model.Open();

    private void Refresh()
    {
        deviceNote.Text = model.DeviceNote;
        saveProblem.Text = model.SaveProblem;
        ShowNotice();

        var settled = !model.Loading && model.LoadProblem is null && !model.NothingConnected;
        rows.Visibility = Shown(settled);
        membersRead.Visibility = Shown(settled && !model.CanEdit);
        advanced.Visibility = Shown(settled || model.NothingConnected);

        if (!settled)
        {
            rows.Children.Clear();
            shown.Clear();
        }

        foreach (var row in model.Rows)
        {
            if (!shown.TryGetValue(row.Slot, out var view))
            {
                view = shown[row.Slot] = new(model, row.Slot);
                rows.Children.Add(view);
            }

            view.Show(row, model);
        }

        ShowAdvanced();
    }

    // The state that takes the rows' place is made once for each state, so its button keeps the focus.
    private void ShowNotice()
    {
        var state = model.LoadProblem is { } problem ? $"failed:{problem}"
            : model.Loading ? "loading"
            : model.NothingConnected ? "nothing"
            : null;

        if (state == noticeShown)
        {
            return;
        }

        noticeShown = state;
        notice.Content = state switch
        {
            null => null,
            "loading" => PageParts.Waiting(VoiceWords.Loading),
            "nothing" => NeedsModel(),
            _ => Failed(),
        };
    }

    private static StackPanel NeedsModel()
    {
        var sentence = Styles.Text(VoiceWords.NeedsModel);
        var connect = new Link { Text = VoiceWords.ConnectOne, Address = Addresses.Models };
        AutomationProperties.SetAutomationId(sentence, "VoiceNeedsModel");
        AutomationProperties.SetAutomationId(connect, "VoiceConnectOne");
        return PageParts.Column(sentence, connect);
    }

    private EmptyState Failed()
    {
        var failed = new EmptyState { Title = VoiceWords.CouldNotLoad, Cause = model.LoadProblem ?? string.Empty, ActionLabel = "Try again" };
        failed.Action += async (_, _) => await model.Load();
        return failed;
    }

    // The limits in effect, read only, and for an owner or admin the way to mark a model by hand.
    private void ShowAdvanced()
    {
        var body = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        foreach (var limit in model.Limits)
        {
            body.Children.Add(Styles.Text(limit, "DvTextSecondary"));
        }

        if (model.CanEdit)
        {
            body.Children.Add(new Link { Text = VoiceWords.MarkByHand, Address = Addresses.Models });
        }

        advanced.Body = body;
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    #endregion
}
