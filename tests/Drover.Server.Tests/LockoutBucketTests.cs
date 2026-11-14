namespace Drover.Server.Tests;

/// <summary>
/// Wrong passwords are counted per account in two buckets: from browsers that have signed in to the account before,
/// and from everyone else. Strangers can lock only their own bucket, and guessing stays bounded in both.
/// </summary>
public sealed class LockoutBucketTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Public Methods

    [Fact]
    public async Task Five_wrong_sign_ins_from_five_addresses_do_not_keep_the_owner_out_of_a_browser_that_signed_in_before()
    {
        var person = await SeedPerson("alice");
        var laptop = NewClient("192.0.2.10");
        await SignInOnce(laptop, person.Email, person.Password);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var stranger = NewClient($"198.51.100.{attempt + 1}");
            (await stranger.SignIn(person.Email, "not-the-password-1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await laptop.SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await NewClient("198.51.100.9").SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Guessing_from_a_browser_that_signed_in_before_is_bounded_too_and_locks_only_its_own_bucket()
    {
        var person = await SeedPerson("alice");
        var laptop = NewClient();
        await SignInOnce(laptop, person.Email, person.Password);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await laptop.SignIn(person.Email, "not-the-password-1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await laptop.SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await NewClient().SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
        Time.Advance(TimeSpan.FromMinutes(6));
        (await laptop.SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_browser_that_signed_in_to_another_account_counts_as_a_stranger()
    {
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        var bobsLaptop = NewClient();
        await SignInOnce(bobsLaptop, bob.Email, bob.Password);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await NewClient().SignIn(alice.Email, "not-the-password-1");
        }

        (await bobsLaptop.SignIn(alice.Email, alice.Password)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_changed_marker_counts_as_a_stranger()
    {
        var person = await SeedPerson("alice");
        var laptop = NewClient();
        await SignInOnce(laptop, person.Email, person.Password);
        var forged = NewClient();
        forged.SetCookie(KnownBrowser.CookieName, $"{laptop.Cookie(KnownBrowser.CookieName)![..^4]}AAAA");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await NewClient().SignIn(person.Email, "not-the-password-1");
        }

        (await forged.SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await laptop.SignIn(person.Email, person.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_marker_is_http_only_secure_strict_and_host_bound_and_is_not_a_credential()
    {
        var person = await SeedPerson("alice");
        var laptop = NewClient();

        var response = await laptop.SignIn(person.Email, person.Password);
        var onlyTheMarker = NewClient();
        onlyTheMarker.SetCookie(KnownBrowser.CookieName, laptop.Cookie(KnownBrowser.CookieName)!);

        var cookie = response.Headers.GetValues("Set-Cookie").Single(line => line.StartsWith($"{KnownBrowser.CookieName}=", StringComparison.Ordinal));
        cookie.ToLowerInvariant().Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/");
        KnownBrowser.CookieName.Should().StartWith("__Host-");
        laptop.Cookie(KnownBrowser.CookieName).Should().NotContain(person.UserId.ToString("N"));
        (await onlyTheMarker.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Private Methods

    /// <summary>Signs in and out again, leaving the browser with its marker and a fresh antiforgery token, as after sign-out.</summary>
    private static async Task SignInOnce(ApiClient browser, string email, string password)
    {
        (await browser.SignIn(email, password)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await browser.Post("/api/sign-out")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await browser.Get("/api/antiforgery")).EnsureSuccessStatusCode();
    }

    #endregion
}
