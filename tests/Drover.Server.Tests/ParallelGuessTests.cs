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

    #endregion

    #region Private Methods

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
