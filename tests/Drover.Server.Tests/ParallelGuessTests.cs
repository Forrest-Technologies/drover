namespace Drover.Server.Tests;

/// <summary>
/// Wrong passwords sent at the same moment are counted one after another, so guessing in parallel gets no more
/// checks against the real password than guessing in turn, in either lockout bucket, and no attempt fails with 500.
/// </summary>
public sealed class ParallelGuessTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const int Guesses = 20;

    private const int Limit = 5;

    #endregion

    #region Public Methods

    [Fact]
    public async Task Parallel_wrong_guesses_from_strangers_get_no_more_checks_than_the_limit_and_lock_the_account()
    {
        var person = await SeedPerson("alice");
        var hash = await PasswordHashOf(person.UserId);
        List<ApiClient> strangers =
            [.. Enumerable.Range(0, Guesses).Select(index => NewClient($"198.51.100.{index + 1}"))];
        await Task.WhenAll(strangers.Select(stranger => stranger.Get("/api/antiforgery")));

        var statuses = await Task.WhenAll(strangers.Select(async stranger =>
            (await stranger.SignIn(person.Email, "not-the-password-1")).StatusCode));

        statuses.Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
        Hashes.ChecksAgainst(hash).Should().BeLessThanOrEqualTo(Limit);
        (await NewClient().SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Parallel_wrong_guesses_from_a_browser_that_signed_in_before_get_no_more_checks_than_the_limit()
    {
        var person = await SeedPerson("alice");
        var laptop = NewClient();
        (await laptop.SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await laptop.Post("/api/sign-out")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var hash = await PasswordHashOf(person.UserId);
        var before = Hashes.ChecksAgainst(hash);
        List<ApiClient> copies = [.. Enumerable.Range(0, Guesses).Select(_ => NewClient())];

        foreach (var copy in copies)
        {
            copy.SetCookie(KnownBrowser.CookieName, laptop.Cookie(KnownBrowser.CookieName)!);
        }

        await Task.WhenAll(copies.Select(copy => copy.Get("/api/antiforgery")));

        var statuses = await Task.WhenAll(copies.Select(async copy =>
            (await copy.SignIn(person.Email, "not-the-password-1")).StatusCode));

        statuses.Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
        (Hashes.ChecksAgainst(hash) - before).Should().BeLessThanOrEqualTo(Limit);
        (await laptop.Get("/api/antiforgery")).EnsureSuccessStatusCode();
        (await laptop.SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wrong_guesses_and_the_right_password_at_once_never_fail_with_500_and_leave_the_account_locked()
    {
        var person = await SeedPerson("alice");
        List<(ApiClient Client, string Password)> attempts =
        [
            .. Enumerable.Range(0, Guesses)
                .Select(index => (NewClient($"198.51.100.{index + 1}"), "not-the-password-1")),
            (NewClient("198.51.100.200"), person.Password),
        ];
        await Task.WhenAll(attempts.Select(attempt => attempt.Client.Get("/api/antiforgery")));

        var statuses = await Task.WhenAll(attempts.Select(async attempt =>
            (await attempt.Client.SignIn(person.Email, attempt.Password)).StatusCode));

        statuses.Should().NotContain(HttpStatusCode.InternalServerError);
        statuses[..Guesses].Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
        (await NewClient().SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_strangers_attempt_in_the_middle_of_its_hash_does_not_hold_up_the_owners_browser()
    {
        var person = await SeedPerson("alice");
        var laptop = NewClient();
        (await laptop.SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await laptop.Post("/api/sign-out")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await laptop.Get("/api/antiforgery")).EnsureSuccessStatusCode();
        var stranger = NewClient("198.51.100.1");
        (await stranger.Get("/api/antiforgery")).EnsureSuccessStatusCode();

        using var hold = Hashes.HoldNextCheck();
        var strangerAttempt = stranger.SignIn(person.Email, "not-the-password-1");
        await hold.Entered.WaitAsync(TimeSpan.FromSeconds(10), Token);

        // The owner's sign-in reads the account and inserts a session that references it.
        var owner = await laptop.SignIn(person.Email, person.Password).WaitAsync(TimeSpan.FromSeconds(10), Token);
        hold.Dispose();

        owner.StatusCode.Should().Be(HttpStatusCode.OK);
        (await strangerAttempt).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_sign_in_holds_no_transaction_while_it_hashes_whether_or_not_the_email_is_known(bool known)
    {
        var person = await SeedPerson("alice");
        var email = known ? person.Email : "nobody@example.test";
        var stranger = NewClient("198.51.100.1");
        (await stranger.Get("/api/antiforgery")).EnsureSuccessStatusCode();

        using var hold = Hashes.HoldNextCheck();
        var attempt = stranger.SignIn(email, "not-the-password-1");
        await hold.Entered.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await Task.Delay(400, Token);
        var longest = await LongestOpenTransaction();
        hold.Dispose();

        (await attempt).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        longest.Should().BeLessThan(
            TimeSpan.FromMilliseconds(300),
            "no transaction, and so no row lock, spans the hash");
    }

    [Fact]
    public async Task Eight_parallel_attempts_on_a_known_and_on_an_unknown_email_take_the_same_steps()
    {
        var person = await SeedPerson("alice");
        List<ApiClient> clients = [.. Enumerable.Range(0, 16).Select(index => NewClient($"198.51.100.{index + 1}"))];
        await Task.WhenAll(clients.Select(client => client.Get("/api/antiforgery")));

        var statuses = await Task.WhenAll(clients.Select(async (client, index) =>
        {
            var email = index % 2 == 0 ? person.Email : "nobody@example.test";
            return (await client.SignIn(email, "not-the-password-1")).StatusCode;
        }));

        statuses.Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
        Hashes.Checks.Should().Be(16, "every attempt hashes its guess once, known email or not, locked or not");
    }

    #endregion

    #region Private Methods

    /// <summary>The age of the oldest transaction another connection to this test's database has open now.</summary>
    private async Task<TimeSpan> LongestOpenTransaction()
    {
        await using var connection = new NpgsqlConnection(AppConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            """
            SELECT coalesce(max(extract(epoch FROM clock_timestamp() - xact_start)), 0)::float8
            FROM pg_stat_activity
            WHERE datname = current_database() AND pid <> pg_backend_pid() AND xact_start IS NOT NULL
            """,
            connection);
        return TimeSpan.FromSeconds((double)(await command.ExecuteScalarAsync(Token))!);
    }

    private async Task<string> PasswordHashOf(Guid userId)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetUser(userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(userId.ToString());
        await work.Commit(Token);
        return user!.PasswordHash!;
    }

    #endregion
}
