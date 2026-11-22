namespace Drover.Server.Tests;

/// <summary>
/// The theme a person keeps for every device that follows their account, through the API. It is one person's alone,
/// in every space, and the server keeps only a well formed theme id and mode.
/// </summary>
public sealed class AppearanceApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string Path = "/api/account/appearance";

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_person_who_keeps_no_theme_is_told_so()
    {
        var alice = await SignIn(await SeedPerson("alice"));

        (await alice.Get(Path)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_kept_theme_is_read_back_and_a_new_one_replaces_it()
    {
        var alice = await SignIn(await SeedPerson("alice"));

        var first = await alice.Put(Path, new AppearanceSetting("night-yard", "light"));
        var second = await alice.Put(Path, new AppearanceSetting("paper-ledger", "system"));

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetFor<AppearanceSetting>(alice, Path)).Should().Be(new AppearanceSetting("paper-ledger", "system"));
    }

    [Fact]
    public async Task One_persons_theme_is_never_another_persons_even_in_the_same_space()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var asAlice = await SignIn(alice);
        var asMember = await SignIn(member.Email, member.Password);

        await asAlice.Put(Path, new AppearanceSetting("foundry", "dark"));

        (await asMember.Get(Path)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await asMember.Put(Path, new AppearanceSetting("blueprint", "light"));
        (await GetFor<AppearanceSetting>(asAlice, Path)).Should().Be(new AppearanceSetting("foundry", "dark"));
    }

    [Theory]
    [InlineData("Night Yard", "dark")]
    [InlineData("night_yard", "dark")]
    [InlineData("https://example.test/theme", "dark")]
    [InlineData("", "dark")]
    [InlineData("night-yard", "dim")]
    [InlineData("night-yard", "DARK")]
    [InlineData("an-id-that-runs-well-past-the-forty-characters-a-theme-may-have", "dark")]
    public async Task Anything_but_a_theme_id_and_a_mode_is_refused_and_nothing_is_kept(string themeId, string mode)
    {
        var alice = await SignIn(await SeedPerson("alice"));

        (await alice.Put(Path, new AppearanceSetting(themeId, mode))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await alice.Get(Path)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Someone_not_signed_in_is_told_nothing_and_keeps_nothing()
    {
        var stranger = NewClient();

        (await stranger.Get(Path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var kept = await stranger.Put(Path, new AppearanceSetting("night-yard", "dark"));
        kept.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
