namespace Drover.Presentation.Tests.Session;

/// <summary>
/// The space the screens work in. A tapped notification names the space of its conversation, and a conversation outside
/// the person's own space must open there and not as not found, while an address can never take a person into a space
/// they do not belong to.
/// </summary>
public sealed class SessionStateSpaceTests : IDisposable
{
    #region Private Fields

    private static readonly Guid personal = Guid.Parse("00000000-0000-7000-8000-0000000000b1");

    private static readonly Guid shared = Guid.Parse("00000000-0000-7000-8000-0000000000b2");

    private readonly FakeServer server = new();

    #endregion

    #region Public Methods

    public void Dispose() => server.Dispose();

    [Fact]
    public async Task The_personal_space_is_the_one_the_screens_work_in_until_another_is_chosen()
    {
        var session = await SignedIn();

        session.Space!.Id.Should().Be(personal);
        session.ChooseSpace(shared);
        session.Space!.Id.Should().Be(shared);
        session.ChooseSpace(null);
        session.Space!.Id.Should().Be(personal);
    }

    [Fact]
    public async Task A_space_the_person_is_not_in_is_ignored()
    {
        var session = await SignedIn();

        session.ChooseSpace(Guid.NewGuid());

        session.Space!.Id.Should().Be(personal);
    }

    [Fact]
    public async Task A_space_chosen_before_the_session_is_read_applies_once_it_is()
    {
        var session = new SessionState(server.CreateApi(), new ShareInbox());
        session.ChooseSpace(shared);
        server.Answer("GET", "/api/session", Session());

        await session.Load(TestContext.Current.CancellationToken);

        session.Space!.Id.Should().Be(shared);
    }

    [Fact]
    public async Task Signing_out_goes_back_to_the_personal_space()
    {
        var session = await SignedIn();
        server.Answer("POST", "/api/sign-out", HttpStatusCode.NoContent);
        session.ChooseSpace(shared);

        await session.End(TestContext.Current.CancellationToken);
        server.Answer("GET", "/api/session", Session());
        session.Begin(Session());

        session.Space!.Id.Should().Be(personal);
    }

    [Fact]
    public async Task Choosing_a_space_tells_the_screens_so_they_read_again()
    {
        var session = await SignedIn();
        var changes = 0;
        session.Changed += () => changes++;

        session.ChooseSpace(shared);
        session.ChooseSpace(shared);
        session.ChooseSpace(null);

        changes.Should().Be(2, "choosing the space already chosen changes nothing");
    }

    [Fact]
    public void An_opening_keeps_its_address_with_its_space_and_is_taken_once()
    {
        var navigation = new IncomingNavigation();
        var thread = Guid.NewGuid();

        navigation.Open(Addresses.SideChat(Guid.NewGuid(), thread), shared);
        var taken = navigation.TakeOpening();

        taken!.Space.Should().Be(shared);
        taken.Path.Should().Contain(thread.ToString());
        navigation.TakeOpening().Should().BeNull();
    }

    [Fact]
    public void An_address_without_a_space_opens_in_whatever_space_the_person_is_in()
    {
        var navigation = new IncomingNavigation();

        navigation.Open(Addresses.ToolSignInReturn("ticket"));

        navigation.TakeOpening()!.Space.Should().BeNull();
    }

    #endregion

    #region Private Methods

    private async Task<SessionState> SignedIn()
    {
        server.Answer("GET", "/api/session", Session());
        var session = new SessionState(server.CreateApi(), new ShareInbox());
        await session.Load(TestContext.Current.CancellationToken);
        return session;
    }

    private static SessionResponse Session() =>
        new(Guid.Parse("00000000-0000-7000-8000-0000000000a1"), "sample@example.test", "Sample person",
        [
            new(shared, "shared", "Sample team", "team", "member"),
            new(personal, "personal", "Sample space", "sample", "owner"),
        ]);

    #endregion
}
