namespace Drover.Server.Tests;

public sealed class SpaceAccessTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Properties

    /// <summary>This test reads the SQL the server ran from the log, so it turns the command category back up.</summary>
    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Information",
    };

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_member_reads_their_own_space()
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();
        await client.SignIn(alice.Email, alice.Password);

        var response = await client.Get($"/api/spaces/{alice.SpaceId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var space = await response.Content.ReadFromJsonAsync<SpaceResponse>(Token);
        space!.Id.Should().Be(alice.SpaceId);
        space.Role.Should().Be("owner");
    }

    [Fact]
    public async Task Another_persons_space_and_its_resources_are_404_and_no_space_data_is_read_first()
    {
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        await SeedSecret(bob.SpaceId, bob.UserId, "bobs-secret-value-91ac");
        var client = NewClient();
        await client.SignIn(alice.Email, alice.Password);
        var before = Logs.Entries.Count;

        var space = await client.Get($"/api/spaces/{bob.SpaceId}");
        var secrets = await client.Get($"/api/spaces/{bob.SpaceId}/secrets");
        var missing = await client.Get($"/api/spaces/{Guid.CreateVersion7()}");

        space.StatusCode.Should().Be(HttpStatusCode.NotFound);
        secrets.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await space.Content.ReadAsStringAsync(Token)).Should().Be(await missing.Content.ReadAsStringAsync(Token));
        var sql = Logs.Entries.Skip(before)
            .Where(entry => entry.Category == "Microsoft.EntityFrameworkCore.Database.Command")
            .Select(entry => entry.Text)
            .ToList();
        sql.Should().NotBeEmpty();
        sql.Should().OnlyContain(text => !text.Contains("FROM spaces", StringComparison.Ordinal)
            && !text.Contains("FROM secrets", StringComparison.Ordinal)
            && !text.Contains("FROM bots", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_request_with_no_session_is_401_before_anything_else()
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();

        (await client.Get($"/api/spaces/{alice.SpaceId}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Listing_secrets_needs_an_admin_or_an_owner()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var admin = await SeedMember(alice.SpaceId, "admin", MembershipRole.Admin);
        var asMember = NewClient();
        var asAdmin = NewClient();
        await asMember.SignIn(member.Email, member.Password);
        await asAdmin.SignIn(admin.Email, admin.Password);

        (await asMember.Get($"/api/spaces/{alice.SpaceId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await asMember.Get($"/api/spaces/{alice.SpaceId}/secrets")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await asAdmin.Get($"/api/spaces/{alice.SpaceId}/secrets")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_secrets_list_says_a_value_exists_and_never_returns_it()
    {
        var alice = await SeedPerson("alice");
        const string SecretValue = "alices-model-key-5b2f77d0";
        var id = await SeedSecret(alice.SpaceId, alice.UserId, SecretValue);
        var client = NewClient();
        await client.SignIn(alice.Email, alice.Password);

        var response = await client.Get($"/api/spaces/{alice.SpaceId}/secrets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync(Token);
        text.Should().NotContain(SecretValue);
        var secrets = JsonSerializer.Deserialize<List<SecretSummary>>(text, JsonSerializerOptions.Web)!;
        secrets.Should().ContainSingle().Which.Should().Match<SecretSummary>(secret => secret.Id == id && secret.HasValue);
    }

    [Fact]
    public async Task Seeded_secret_values_and_passwords_never_appear_in_the_captured_logs()
    {
        var alice = await SeedPerson("alice");
        const string SecretValue = "alices-model-key-5b2f77d0";
        await SeedSecret(alice.SpaceId, alice.UserId, SecretValue);
        var client = NewClient();
        await client.SignIn(alice.Email, alice.Password);
        await client.SignIn(alice.Email, "a-wrong-password-9d41");
        await client.Get($"/api/spaces/{alice.SpaceId}/secrets");
        await client.Get($"/api/spaces/{alice.SpaceId}");

        var logs = Logs.AllText();

        logs.Should().NotBeEmpty();
        logs.Should().NotContain(SecretValue).And.NotContain(alice.Password).And.NotContain("a-wrong-password-9d41");
    }

    #endregion
}
