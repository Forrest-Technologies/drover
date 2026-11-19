namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// A screen opened while the server cannot be reached: the failure stays with the screen's gate, which says so and
/// opens the screen again when asked to try again. Nothing escapes to the platform, where it would close the app.
/// </summary>
public sealed class ScreenOpeningTests : ViewModelTest
{
    #region Private Fields

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly string space = $"/api/spaces/{SpaceId}";

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_conversation_opened_while_the_server_is_unreachable_shows_the_gate_and_does_not_throw()
    {
        using var ui = new UiLoop();
        using var talk = new TalkViewModel(Server.CreateApi(), Session, Navigator, Toasts, Inbox, ui, NullLoggerFactory.Instance);

        await OpenedWhileUnreachable(() => talk.Open(new(botId)));
    }

    [Fact]
    public async Task Tool_connections_opened_while_the_server_is_unreachable_show_the_gate_and_do_not_throw() =>
        await OpenedWhileUnreachable(() => Make<ToolConnectionsViewModel>().Open());

    [Fact]
    public async Task The_bot_editor_opened_while_the_server_is_unreachable_shows_the_gate_and_does_not_throw() =>
        await OpenedWhileUnreachable(() => Make<BotEditorViewModel>().Open(botId, null));

    [Fact]
    public async Task Trying_again_after_the_server_returns_opens_the_screen_again()
    {
        var tools = Make<ToolConnectionsViewModel>();
        var gate = Watched();
        var reopened = Task.CompletedTask;
        Navigator.Navigated += (_, _) => reopened = ScreenOpen.At(Session, () => tools.Open(), NullLogger.Instance);
        Server.Unreachable = true;
        await ScreenOpen.At(Session, () => tools.Open(), NullLogger.Instance);
        await gate.Check();
        gate.State.Should().Be(GateState.Unreachable);

        Server.Unreachable = false;
        SignedIn();
        Server.Answer("GET", $"{space}/tool-connections", Array.Empty<ToolConnectionSummary>());
        await gate.TryAgainCommand.ExecuteAsync(null);
        await reopened;

        gate.State.Should().Be(GateState.SignedIn);
        Session.Unreachable.Should().BeFalse();
        tools.Connections.Should().BeEmpty("the screen loaded what it shows once the server answered");
        Navigator.CanGoBack.Should().BeFalse("opening the screen again is not a step to go back through");
    }

    [Fact]
    public async Task A_gate_shown_after_its_screen_failed_keeps_saying_so_while_the_session_is_known()
    {
        SignedIn();
        await Session.Load(TestContext.Current.CancellationToken);
        await ScreenOpen.At(
            Session,
            () => Task.FromException(new HttpRequestException("Sample refusal.")),
            NullLogger.Instance);
        var gate = Watched();

        await gate.Check();

        gate.State.Should().Be(
            GateState.Unreachable,
            "a known session does not mean the screen behind the gate loaded");
    }

    [Fact]
    public async Task Opening_a_screen_lets_a_failure_that_is_not_the_servers_escape()
    {
        var opening = () => ScreenOpen.At(
            Session,
            () => throw new InvalidOperationException("Sample fault."),
            NullLogger.Instance);

        await opening.Should().ThrowAsync<InvalidOperationException>(
            "a fault in the app is a bug to see, not a server to blame");
        Session.Unreachable.Should().BeFalse();
    }

    #endregion

    #region Private Methods

    private SessionGateViewModel Watched()
    {
        var gate = Make<SessionGateViewModel>();
        gate.Watch();
        return gate;
    }

    private async Task OpenedWhileUnreachable(Func<Task> open)
    {
        Server.Unreachable = true;
        var gate = Watched();

        var opening = () => ScreenOpen.At(Session, open, NullLogger.Instance);

        await opening.Should().NotThrowAsync();
        await gate.Check();
        gate.State.Should().Be(GateState.Unreachable);
        Session.Unreachable.Should().BeTrue();
    }

    #endregion
}
