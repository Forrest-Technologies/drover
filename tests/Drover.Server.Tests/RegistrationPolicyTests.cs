namespace Drover.Server.Tests;

/// <summary>
/// Registration on a self-hosted server whose operator opened it, forbade private addresses as that requires, and
/// asked for the terms to be accepted. The version accepted is recorded with the account.
/// </summary>
public sealed class RegistrationPolicyTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string Terms = "2026-11";

    #endregion

    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Drover:Policy:Rules:OpenRegistration"] = "true",

        // Open registration on a self-hosted server must forbid private addresses, or the server does not start.
        ["Drover:Policy:Rules:AllowPrivateAddresses"] = "false",
        ["Drover:Policy:Rules:RequireTermsAcceptance"] = "true",
        ["Drover:Policy:TermsVersion"] = Terms,
    };

    #endregion

    #region Public Methods

    [Fact]
    public async Task Registration_stays_closed_until_the_first_account_exists()
    {
        var client = NewClient("127.0.0.1");

        var status = await client.Get("/api/register");
        var attempt = await client.Post("/api/register", new RegisterRequest("a@example.test", "A", NewPassword(), Terms));

        status.StatusCode.Should().Be(HttpStatusCode.NotFound);
        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Registration_names_the_terms_version_to_accept()
    {
        await SetUpFirstAccount();

        var status = await (await NewClient().Get("/api/register")).Content.ReadFromJsonAsync<RegistrationStatus>(Token);

        status!.TermsVersion.Should().Be(Terms);
    }

    [Fact]
    public async Task Registering_without_accepting_the_current_terms_is_refused()
    {
        await SetUpFirstAccount();
        var client = NewClient();

        var missing = await client.Post("/api/register", new RegisterRequest("a@example.test", "A", NewPassword(), null));
        var old = await client.Post("/api/register", new RegisterRequest("a@example.test", "A", NewPassword(), "2025-01"));

        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        old.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await old.Content.ReadAsStringAsync(Token)).Should().Contain(Terms);
    }

    [Fact]
    public async Task Registering_with_the_current_terms_creates_the_account_and_records_the_version_accepted()
    {
        await SetUpFirstAccount();
        var client = NewClient();

        var response = await client.Post("/api/register", new RegisterRequest("a@example.test", "A", NewPassword(), Terms));
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>(Token);
        var user = await ReadUser(session!.UserId);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        session.Spaces.Should().ContainSingle().Which.Role.Should().Be("owner");
        user.TermsVersion.Should().Be(Terms);
        user.TermsAcceptedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Registering_with_an_email_that_is_taken_is_refused()
    {
        var (_, first, _) = await SetUpFirstAccount("taken@example.test");
        var client = NewClient();

        var response = await client.Post("/api/register", new RegisterRequest("taken@example.test", "Again", NewPassword(), Terms));

        first.Email.Should().Be("taken@example.test");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Private Methods

    private async Task<User> ReadUser(Guid userId)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetUser(userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(userId.ToString());
        await work.Commit(Token);
        return user!;
    }

    #endregion
}

/// <summary>
/// A hosted server whose operator opened registration. Email verification is required there, and the server cannot
/// send email yet, so it says so instead of creating an account nobody verified.
/// </summary>
public sealed class HostedRegistrationTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings
    {
        get
        {
            var settings = new Dictionary<string, string?>(base.Settings)
            {
                ["DROVER_POLICY"] = DeploymentPolicy.HostedName,
                ["Drover:Policy:Rules:OpenRegistration"] = "true",
                ["Drover:Policy:TermsVersion"] = "2026-11",
            };

            foreach (var (key, value) in KeyRingCertificate.Settings)
            {
                settings[key] = value;
            }

            return settings;
        }
    }

    #endregion

    #region Public Methods

    [Fact]
    public async Task Open_registration_asks_for_terms_and_refuses_to_create_an_unverified_account()
    {
        await SeedPerson("operator");
        var client = NewClient();

        var status = await (await client.Get("/api/register")).Content.ReadFromJsonAsync<RegistrationStatus>(Token);
        var attempt = await client.Post("/api/register", new RegisterRequest("a@example.test", "A", NewPassword(), "2026-11"));

        status!.TermsVersion.Should().Be("2026-11");
        attempt.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        (await attempt.Content.ReadAsStringAsync(Token)).Should().Contain("email verification");
    }

    #endregion
}
