namespace Drover.Presentation.Tests.ViewModels;

/// <summary>The view models of the shell: the session gate, sign-in, the account menu, the theme, and the toasts.</summary>
public sealed class ShellViewModelTests : IDisposable
{
    #region Private Fields

    private readonly FakeServer server = new();

    private readonly Navigator navigator = new();

    private readonly MemorySettings settings = new();

    private readonly ToastService toasts = new(TimeProvider.System);

    private readonly SessionState session;

    private readonly ThemeModel theme;

    private readonly ServiceProvider services;

    #endregion

    #region Constructors

    public ShellViewModelTests()
    {
        session = new(server.CreateApi(), new ShareInbox());
        theme = new(settings, NullLogger<ThemeModel>.Instance);
        services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(server.CreateApi())
            .AddSingleton(session)
            .AddSingleton(navigator)
            .AddSingleton(toasts)
            .AddSingleton(theme)
            .BuildServiceProvider();
    }

    #endregion

    #region Public Methods

    public void Dispose()
    {
        services.Dispose();
        server.Dispose();
    }

    [Fact]
    public async Task The_gate_lets_a_signed_in_person_through()
    {
        server.Answer("GET", "/api/session", Samples.Session());
        var gate = Make<SessionGateViewModel>();

        gate.State.Should().Be(GateState.Checking);
        await gate.Check();

        gate.State.Should().Be(GateState.SignedIn);
        navigator.Current.Should().Be(Addresses.Home);
    }

    [Fact]
    public async Task Loading_the_session_tells_live_services_who_is_signed_in()
    {
        server.Answer("GET", "/api/session", Samples.Session());
        var changes = 0;
        session.Changed += () => changes++;

        await session.Load(TestContext.Current.CancellationToken);

        changes.Should().Be(1);
        session.Space.Should().NotBeNull();
    }

    [Fact]
    public async Task The_gate_sends_a_person_with_no_session_to_sign_in()
    {
        server.Answer("GET", "/api/session", HttpStatusCode.Unauthorized);
        var gate = Make<SessionGateViewModel>();

        await gate.Check();

        navigator.Current.Should().Be(Addresses.SignIn);
        navigator.CanGoBack.Should().BeFalse("the screen that needed a session is not a place to go back to");
    }

    [Fact]
    public async Task The_gate_sends_the_first_person_on_a_new_server_to_setup()
    {
        server.Answer("GET", "/api/session", HttpStatusCode.Unauthorized);
        server.Answer("GET", "/api/setup", new SetupStatus(RequiresCode: false));
        var gate = Make<SessionGateViewModel>();

        await gate.Check();

        navigator.Current.Should().Be(Addresses.Setup);
    }

    [Fact]
    public async Task The_gate_says_so_when_the_server_does_not_answer_and_can_try_again()
    {
        server.Unreachable = true;
        var gate = Make<SessionGateViewModel>();

        await gate.Check();
        gate.State.Should().Be(GateState.Unreachable);

        server.Unreachable = false;
        server.Answer("GET", "/api/session", Samples.Session());
        await gate.CheckCommand.ExecuteAsync(null);

        gate.State.Should().Be(GateState.SignedIn);
    }

    [Fact]
    public async Task Signing_in_sends_the_trimmed_email_begins_the_session_and_goes_to_the_start()
    {
        server.Answer("POST", "/api/sign-in", Samples.Session());
        var signIn = Make<SignInViewModel>();
        signIn.Email = "  sample@example.test ";
        signIn.Password = "sample password";

        await signIn.Submit();

        server.Sent("POST", "/api/sign-in").Single().Should().Contain("\"sample@example.test\"");
        session.Current!.DisplayName.Should().Be("Sample person");
        navigator.Current.Should().Be(Addresses.Home);
        signIn.Problem.Should().BeNull();
        signIn.Busy.Should().BeFalse();
    }

    [Fact]
    public async Task A_refused_sign_in_says_what_the_server_said_and_stays()
    {
        server.Refuse("POST", "/api/sign-in", HttpStatusCode.Unauthorized, "That email and password do not match an account.");
        var signIn = Make<SignInViewModel>();

        await signIn.Submit();

        signIn.Problem.Should().Be("That email and password do not match an account.");
        session.Current.Should().BeNull();
        navigator.Current.Should().Be(Addresses.Home);
        signIn.SubmitLabel.Should().Be("Sign in");
    }

    [Fact]
    public async Task The_sign_in_button_says_it_is_working_while_the_server_is_asked()
    {
        var held = server.Hold("POST", "/api/sign-in");
        var signIn = Make<SignInViewModel>();

        var submit = signIn.Submit();
        signIn.Busy.Should().BeTrue();
        signIn.SubmitLabel.Should().Be("Signing in");

        held.SetResult();
        await submit;
        signIn.Busy.Should().BeFalse();
    }

    [Fact]
    public async Task Someone_already_signed_in_is_sent_on_from_the_sign_in_screen()
    {
        server.Answer("GET", "/api/session", Samples.Session());
        navigator.NavigateTo(Addresses.SignIn);

        await Make<SignInViewModel>().Load();

        navigator.Current.Should().Be(Addresses.Home);
    }

    [Fact]
    public async Task A_new_server_sends_the_sign_in_screen_to_setup()
    {
        server.Answer("GET", "/api/session", HttpStatusCode.Unauthorized);
        server.Answer("GET", "/api/setup", new SetupStatus(RequiresCode: true));

        await Make<SignInViewModel>().Load();

        navigator.Current.Should().Be(Addresses.Setup);
    }

    [Fact]
    public void The_account_menu_carries_the_persons_name_and_is_named_in_full_when_compact()
    {
        session.Begin(Samples.Session());
        using var menu = Make<AccountMenuViewModel>();

        menu.Label.Should().Be("Sample person");
        menu.AccessibleLabel.Should().BeNull();

        menu.Compact = true;
        menu.Label.Should().Be("Account");
        menu.AccessibleLabel.Should().Be("Account for Sample person");
    }

    [Fact]
    public void The_account_menu_marks_the_theme_in_use_in_words_and_changes_it()
    {
        using var menu = Make<AccountMenuViewModel>();
        menu.Themes.Single(option => option.InUse).AccessibleName.Should().Be("Dark theme, in use");

        var changes = 0;
        menu.PropertyChanged += (_, change) => changes += change.PropertyName == nameof(AccountMenuViewModel.Themes) ? 1 : 0;
        menu.ChooseTheme(ThemeChoice.Light);

        theme.Choice.Should().Be(ThemeChoice.Light);
        menu.Themes.Single(option => option.InUse).Name.Should().Be("Light theme");
        changes.Should().Be(1);
        settings.Values[ThemeModel.SettingName].Should().Be("light");
    }

    [Theory]
    [InlineData("owner", true)]
    [InlineData("admin", true)]
    [InlineData("member", false)]
    public void Only_the_people_who_run_the_space_are_shown_status(string role, bool shown)
    {
        session.Begin(Samples.Session(role));
        using var menu = Make<AccountMenuViewModel>();

        menu.ShowsStatus.Should().Be(shown);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_and_goes_to_sign_in()
    {
        session.Begin(Samples.Session());
        server.Answer("POST", "/api/sign-out", HttpStatusCode.NoContent);
        using var menu = Make<AccountMenuViewModel>();

        await menu.SignOut();

        session.Current.Should().BeNull();
        navigator.Current.Should().Be(Addresses.SignIn);
    }

    [Fact]
    public async Task A_sign_out_the_server_did_not_take_says_the_person_is_still_signed_in()
    {
        session.Begin(Samples.Session());
        server.Unreachable = true;
        using var menu = Make<AccountMenuViewModel>();

        await menu.SignOut();

        session.Current.Should().NotBeNull();
        toasts.Toasts.Single().Kind.Should().Be(ToastKind.Error);
        toasts.Toasts.Single().Message.Should().Be("You are still signed in. Drover could not reach its server. Check that the server is running.");
    }

    [Fact]
    public async Task Signing_out_turns_this_devices_notifications_off_first()
    {
        session.Begin(Samples.Session());
        server.Answer("POST", "/api/sign-out", HttpStatusCode.NoContent);
        server.Answer("POST", "/api/push/device/off", HttpStatusCode.NoContent);
        var push = new SubscribedDevice("https://push.example.test/sample");
        using var menu = ActivatorUtilities.CreateInstance<AccountMenuViewModel>(services, push);

        await menu.SignOut();

        server.Requests.Select(request => request.Path).Should().ContainInOrder("/api/push/device/off", "/api/sign-out");
    }

    [Fact]
    public void The_theme_is_dark_until_a_person_chooses_and_the_choice_is_kept_under_the_web_clients_name()
    {
        theme.Load();
        theme.Choice.Should().Be(ThemeChoice.Dark);

        theme.Choose(ThemeChoice.System);
        settings.Values.Should().ContainKey("drover.theme").WhoseValue.Should().Be("system");

        var later = new ThemeModel(settings, NullLogger<ThemeModel>.Instance);
        later.Load();
        later.Choice.Should().Be(ThemeChoice.System);
    }

    [Fact]
    public void A_device_that_refuses_storage_is_dark_and_still_takes_a_choice_for_the_visit()
    {
        settings.Refuses = true;

        theme.Load();
        theme.Choice.Should().Be(ThemeChoice.Dark);

        theme.Choose(ThemeChoice.Light);
        theme.Choice.Should().Be(ThemeChoice.Light);
    }

    [Fact]
    public void The_toast_host_keeps_errors_apart_from_the_quiet_ones_and_names_the_kind_in_a_word()
    {
        using var host = new ToastHostViewModel(toasts);
        var changes = 0;
        host.Changed += () => changes++;

        toasts.Show("Sample: saved.", ToastKind.Success);
        toasts.Show("Sample: check the address.", ToastKind.Error);

        host.Quiet.Should().ContainSingle().Which.Message.Should().Be("Sample: saved.");
        host.Errors.Should().ContainSingle().Which.Message.Should().Be("Sample: check the address.");
        changes.Should().Be(2);
        ToastHostViewModel.KindWord(ToastKind.Error).Should().Be("Error");
        ToastHostViewModel.KindWord(ToastKind.Warning).Should().Be("Warning");
        ToastHostViewModel.KindWord(ToastKind.Info).Should().BeNull();
    }

    [Fact]
    public async Task A_toasts_action_runs_once_and_the_toast_leaves_first()
    {
        using var host = new ToastHostViewModel(toasts);
        var runs = 0;
        toasts.Show("Sample: bot removed.", ToastKind.Warning, new("Undo", () =>
        {
            runs++;
            host.Quiet.Should().BeEmpty("the toast is dismissed before its action runs, so it cannot be chosen twice");
            return Task.CompletedTask;
        }));

        await host.Act(host.Quiet.Single());

        runs.Should().Be(1);
    }

    #endregion

    #region Private Methods

    private T Make<T>() => ActivatorUtilities.CreateInstance<T>(services);

    /// <summary>A device that is subscribed for notifications at one address.</summary>
    private sealed class SubscribedDevice(string endpoint) : IPushRegistration
    {
        public Task<DevicePushCheck> Check(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DevicePushCheck(DevicePushSupport.Available, endpoint));

        public Task<PushDeviceRequest?> Subscribe(string publicKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<PushDeviceRequest?>(null);

        public Task<string?> Unsubscribe(CancellationToken cancellationToken = default) => Task.FromResult<string?>(endpoint);
    }

    #endregion
}
