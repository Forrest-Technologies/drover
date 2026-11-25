namespace Drover.Server.Tests;

/// <summary>Account-wide appearance API behavior and isolation.</summary>
public sealed class AppearanceApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string Path = "/api/account/appearance";

    private static IReadOnlyList<AppearanceSetting> Invalid { get; } =
    [
        new("Front-Office", "dark", "theme"),
        new("front-office\n", "dark", "theme"),
        new("front-office", "dim", "theme"),
        new("front-office", "dark", "purple"),
        new("an-id-that-runs-well-past-the-forty-characters-a-theme-may-have", "dark", "theme"),
    ];

    #endregion

    #region Public Methods

    [Fact]
    public async Task SavedChoice_IsReturnedForSameUser_OnNewSession()
    {
        var person = await SeedPerson("alice");
        var firstSession = await SignIn(person.Email, person.Password);
        var choice = new AppearanceSetting("front-office", "black", "blue");

        (await firstSession.Put(Path, choice)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var newSession = await SignIn(person.Email, person.Password);
        (await GetFor<AppearanceSetting>(newSession, Path)).Should().Be(choice);
    }

    [Fact]
    public async Task OtherUser_CannotReadOrWriteAppearance()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var asAlice = await SignIn(alice.Email, alice.Password);
        var asMember = await SignIn(member.Email, member.Password);
        var aliceChoice = new AppearanceSetting("front-office", "black", "blue");
        var memberChoice = new AppearanceSetting("night-yard", "light", "green");

        (await asAlice.Put(Path, aliceChoice)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await asMember.Get(Path)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asMember.Put(Path, memberChoice)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetFor<AppearanceSetting>(asAlice, Path)).Should().Be(aliceChoice);
        (await GetFor<AppearanceSetting>(asMember, Path)).Should().Be(memberChoice);
    }

    [Fact]
    public async Task Rejects_invalid_theme_mode_accent_and_unauthenticated_requests()
    {
        var stranger = NewClient();
        (await stranger.Get(Path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await stranger.Put(Path, new AppearanceSetting("front-office", "dark"))).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        var alice = await SignIn(await SeedPerson("alice"));
        foreach (var choice in Invalid)
        {
            (await alice.Put(Path, choice)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        var raw = new StringContent(
            """{"themeId":"front-office","mode":"dark","accent":"theme","customCss":"body{}"}""",
            Encoding.UTF8,
            "application/json");
        (await alice.Put(Path, raw)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var oversized = new StringContent(
            JsonSerializer.Serialize(new
            {
                themeId = new string('a', (int)RequestLimits.Account),
                mode = "dark",
                accent = "theme",
            }),
            Encoding.UTF8,
            "application/json");
        (await alice.Put(Path, oversized)).StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await alice.Get(Path)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Private Methods

    private Task<ApiClient> SignIn((string Email, string Password, Guid UserId, Guid SpaceId) person) =>
        SignIn(person.Email, person.Password);

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<T> GetFor<T>(ApiClient client, string path)
    {
        var response = await client.Get(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    #endregion
}
