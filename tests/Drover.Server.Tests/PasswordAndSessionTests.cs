namespace Drover.Server.Tests;

public sealed class PasswordAndSessionTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Sign-Out

    [Fact]
    public async Task A_copied_session_cookie_gets_401_once_its_owner_signs_out()
    {
        var person = await SeedPerson("alice");
        var owner = NewClient();
        await owner.SignIn(person.Email, person.Password);
        var thief = NewClient();
        thief.TakeCookiesOf(owner);
        (await thief.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await owner.Post("/api/sign-out")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await thief.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await thief.Post("/api/account/password", new ChangePasswordRequest(person.Password, NewPassword())))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_ends_only_that_session()
    {
        var person = await SeedPerson("alice");
        var laptop = NewClient();
        var phone = NewClient();
        await laptop.SignIn(person.Email, person.Password);
        await phone.SignIn(person.Email, person.Password);

        await laptop.Post("/api/sign-out");

        (await laptop.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await phone.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Changing The Password

    [Fact]
    public async Task Changing_the_password_ends_every_other_session_and_keeps_this_one()
    {
        var person = await SeedPerson("alice");
        var laptop = NewClient();
        var phone = NewClient();
        await laptop.SignIn(person.Email, person.Password);
        await phone.SignIn(person.Email, person.Password);
        var replacement = NewPassword();

        var changed = await laptop.Post("/api/account/password", new ChangePasswordRequest(person.Password, replacement));

        changed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await phone.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await laptop.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.OK);
        Time.Advance(TimeSpan.FromMinutes(6));
        (await laptop.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await NewClient().SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await NewClient().SignIn(person.Email, replacement)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_wrong_current_password_changes_nothing()
    {
        var person = await SeedPerson("alice");
        var laptop = NewClient();
        var phone = NewClient();
        await laptop.SignIn(person.Email, person.Password);
        await phone.SignIn(person.Email, person.Password);

        var refused = await laptop.Post("/api/account/password", new ChangePasswordRequest("not-the-password-1", NewPassword()));

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await phone.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await NewClient().SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_new_password_that_is_too_short_is_refused_with_the_reason()
    {
        var person = await SeedPerson("alice");
        var client = NewClient();
        await client.SignIn(person.Email, person.Password);

        var refused = await client.Post("/api/account/password", new ChangePasswordRequest(person.Password, "short"));

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync(Token)).Should().Contain("12");
        (await NewClient().SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Changing_the_password_needs_a_session_and_the_antiforgery_token()
    {
        var person = await SeedPerson("alice");
        var stranger = NewClient();
        var client = NewClient();
        await client.SignIn(person.Email, person.Password);

        var anonymous = await stranger.Post("/api/account/password", new ChangePasswordRequest(person.Password, NewPassword()));
        var withoutToken = await client.PostWithoutToken(
            "/api/account/password",
            new ChangePasswordRequest(person.Password, NewPassword()));

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        withoutToken.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Reset By The Operator

    [Fact]
    public async Task A_reset_code_from_the_log_sets_a_new_password_and_ends_every_session()
    {
        var person = await SeedPerson("alice");
        var laptop = NewClient();
        await laptop.SignIn(person.Email, person.Password);
        var code = await IssueResetCode(person.Email);
        var replacement = NewPassword();

        var reset = await NewClient().Post("/api/password-reset", new ResetPasswordRequest(person.Email, code, replacement));

        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await laptop.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await NewClient().SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await NewClient().SignIn(person.Email, replacement)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_reset_code_works_once()
    {
        var person = await SeedPerson("alice");
        var code = await IssueResetCode(person.Email);

        var first = await NewClient().Post("/api/password-reset", new ResetPasswordRequest(person.Email, code, NewPassword()));
        var second = await NewClient().Post("/api/password-reset", new ResetPasswordRequest(person.Email, code, NewPassword()));

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_reset_code_runs_out_after_fifteen_minutes()
    {
        var person = await SeedPerson("alice");
        var code = await IssueResetCode(person.Email);
        Time.Advance(TimeSpan.FromMinutes(16));

        var late = await NewClient().Post("/api/password-reset", new ResetPasswordRequest(person.Email, code, NewPassword()));

        late.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await NewClient().SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_wrong_code_and_an_unknown_email_are_refused_alike()
    {
        var person = await SeedPerson("alice");
        await IssueResetCode(person.Email);

        var wrong = await NewClient().Post("/api/password-reset", new ResetPasswordRequest(person.Email, "AAAA-AAAA-AAAA", NewPassword()));
        var unknown = await NewClient().Post(
            "/api/password-reset",
            new ResetPasswordRequest("nobody@example.test", "AAAA-AAAA-AAAA", NewPassword()));

        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrong.Content.ReadAsStringAsync(Token)).Should().Be(await unknown.Content.ReadAsStringAsync(Token));
        (await NewClient().SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_reset_code_is_stored_hashed_and_a_new_one_replaces_the_old()
    {
        var person = await SeedPerson("alice");
        var first = await IssueResetCode(person.Email);
        var second = await IssueResetCode(person.Email);

        var stored = await StoredTokenValues(person.UserId);
        var stale = await NewClient().Post("/api/password-reset", new ResetPasswordRequest(person.Email, first, NewPassword()));
        var fresh = await NewClient().Post("/api/password-reset", new ResetPasswordRequest(person.Email, second, NewPassword()));

        stored.Should().ContainSingle();
        stored.Single().Should().NotContain(second.Replace("-", string.Empty, StringComparison.Ordinal));
        stale.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        fresh.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Redeeming_a_code_lifts_a_lockout()
    {
        var person = await SeedPerson("alice");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await NewClient().SignIn(person.Email, "not-the-password-1");
        }

        var replacement = NewPassword();
        var code = await IssueResetCode(person.Email);
        await NewClient().Post("/api/password-reset", new ResetPasswordRequest(person.Email, code, replacement));

        (await NewClient().SignIn(person.Email, replacement)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_operator_command_prints_the_code_and_nothing_else_secret()
    {
        var person = await SeedPerson("alice");

        var exitCode = await PasswordResetCommand.Issue(Services, person.Email);
        var unknown = await PasswordResetCommand.Issue(Services, "nobody@example.test");

        exitCode.Should().Be(0);
        unknown.Should().Be(1);
        var line = Logs.Entries.Single(entry => entry.Category == typeof(PasswordResetCommand).FullName && entry.Level == LogLevel.Warning);
        var code = ResetCodeIn(line.Text);
        var reset = await NewClient().Post("/api/password-reset", new ResetPasswordRequest(person.Email, code, NewPassword()));
        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Logs.AllText().Should().NotContain(person.Password);
    }

    #endregion

    #region Private Methods

    private async Task<string> IssueResetCode(string email)
    {
        await using var scope = Services.CreateAsyncScope();
        var code = await scope.ServiceProvider.GetRequiredService<PasswordService>().IssueResetCode(email, Token);
        return code!;
    }

    private async Task<List<string>> StoredTokenValues(Guid userId)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetUser(userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        return await scope.ServiceProvider.GetRequiredService<DroverDbContext>().Database
            .SqlQuery<string>($"SELECT value AS \"Value\" FROM user_tokens")
            .ToListAsync(Token);
    }

    private static string ResetCodeIn(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, "[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}");
        match.Success.Should().BeTrue();
        return match.Value;
    }

    #endregion
}

public sealed class PasswordResetRateLimitTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Drover:SignInRateLimit:PermitLimit"] = "3",
        ["Drover:SignInRateLimit:Window"] = "00:10:00",
    };

    #endregion

    #region Public Methods

    [Fact]
    public async Task Redeeming_a_reset_code_is_rate_limited_per_address_like_sign_in()
    {
        var client = NewClient("198.51.100.4");
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var request = new ResetPasswordRequest("nobody@example.test", "AAAA-AAAA-AAAA", NewPassword());
            statuses.Add((await client.Post("/api/password-reset", request)).StatusCode);
        }

        statuses.Take(3).Should().AllBeEquivalentTo(HttpStatusCode.BadRequest);
        statuses.Skip(3).Should().AllBeEquivalentTo(HttpStatusCode.TooManyRequests);
    }

    #endregion
}
