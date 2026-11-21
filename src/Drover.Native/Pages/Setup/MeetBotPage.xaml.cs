namespace Drover.Native.Pages;

/// <summary>Name the lead of the crew, choose its model, and open its conversation.</summary>
public sealed partial class MeetBotPage : Screen
{
    #region Constructors

    public MeetBotPage()
    {
        InitializeComponent();
        Model.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    #endregion

    #region Properties

    public override string Title => "Meet your first bot";

    public MeetBotViewModel Model { get; } = Make<MeetBotViewModel>();

    #endregion

    #region Private Methods

    protected override Task Opened(RouteMatch<Type> route) => Model.Open();

    private void Refresh()
    {
        Form.Visibility = Model.Loaded && Model.LoadProblem is null && Model.Bot is null && Model.Connection is not null
            ? Visibility.Visible : Visibility.Collapsed;
        Models.Visibility = Model.Models.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Models.Options = [.. Model.Models.Select(entry => new SelectOption(entry.Model, entry.DisplayName))];
        ModelName.Visibility = Model.Models.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ModelName.Hint = Model.ListProblem ?? "This endpoint does not list its models. Type the name it expects.";
        Create.IsEnabled = !Model.Busy;
        Create.Content = Model.Busy ? $"Creating {Model.ShownName}" : $"Create {Model.ShownName}";
        State.Content = ContentState();
    }

    private UIElement? ContentState()
    {
        if (Model.LoadProblem is { } problem)
        {
            return new EmptyState { Title = "Drover could not load the models", Cause = problem, ActionLabel = "Try again", ActionCommand = Model.LoadCommand };
        }

        if (!Model.Loaded)
        {
            var note = Styles.Text("Asking the provider which models it has.", "DvTextSecondary");
            AutomationProperties.SetLiveSetting(note, AutomationLiveSetting.Polite);
            return note;
        }

        if (Model.Bot is { } bot)
        {
            var result = new StackPanel { Spacing = Tokens.Number("DvSpace4") };
            result.Children.Add(new Avatar { BotName = bot.Name, Hue = BotIdentity.Hue(bot.Id), Size = (int)Tokens.Number("DvAvatar96") });
            result.Children.Add(Styles.Text(bot.Name, "DvTextH2"));
            result.Children.Add(Styles.Text(bot.Model, "DvTextMono"));
            result.Children.Add(Styles.Text($"{bot.Name} is ready. It leads your crew: the bots it makes for you, and the ones you add, work beneath it."));
            result.Children.Add(new Link { Text = $"Talk to {bot.Name}", Address = Addresses.Talk(bot.Id) });
            return result;
        }

        return Model.Connection is null
            ? new EmptyState { Title = "No model is connected", Cause = "A bot needs a model to think with, and this space has none yet.", ActionLabel = "Connect a model", ActionHref = Addresses.SetupModel }
            : null;
    }

    private void Submit(object? sender, EventArgs args)
    {
        if (!Model.Busy && Model.Connection is not null)
        {
            Model.CreateCommand.Execute(null);
        }
    }

    #endregion
}
