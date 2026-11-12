namespace Drover.Server.Tests;

public sealed class SessionAndScopeTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Security Stamp

    [Fact]
    public async Task Changing_the_security_stamp_ends_an_existing_session_after_the_check_interval()
    {
        var person = await SeedPerson("alice");
        var client = NewClient();
        await client.SignIn(person.Email, person.Password);
        await ChangeSecurityStamp(person.UserId);

        Time.Advance(TimeSpan.FromMinutes(4));
        var beforeInterval = await client.Get("/api/session");
        Time.Advance(TimeSpan.FromMinutes(2));
        var afterInterval = await client.Get("/api/session");

        beforeInterval.StatusCode.Should().Be(HttpStatusCode.OK);
        afterInterval.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_unchanged_stamp_keeps_the_session_through_every_check()
    {
        var person = await SeedPerson("alice");
        var client = NewClient();
        await client.SignIn(person.Email, person.Password);

        for (var round = 0; round < 3; round++)
        {
            Time.Advance(TimeSpan.FromMinutes(6));
            (await client.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    #endregion

    #region Lockout Timing

    [Fact]
    public async Task A_locked_account_still_has_its_password_hashed_before_it_is_refused()
    {
        var person = await SeedPerson("alice");
        var client = NewClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.SignIn(person.Email, "not-the-password-1");
        }

        var before = Hashes.Checks;
        var locked = await client.SignIn(person.Email, person.Password);
        var afterLocked = Hashes.Checks;
        await client.SignIn("nobody@example.test", "not-the-password-1");

        locked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (afterLocked - before).Should().Be(1);
        (Hashes.Checks - afterLocked).Should().Be(1);
    }

    #endregion

    #region Tenant Scope

    [Fact]
    public async Task A_union_typed_endpoint_that_writes_and_then_returns_400_leaves_nothing_written()
    {
        var person = await SeedPerson("alice");

        var afterSuccess = await RunThroughFilter(person, success: true);
        var afterBadRequest = await RunThroughFilter(person, success: false);

        afterSuccess.Should().Be(1);
        afterBadRequest.Should().Be(1);
    }

    #endregion

    #region Private Methods

    private async Task ChangeSecurityStamp(Guid userId)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetUser(userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByIdAsync(userId.ToString());
        (await users.UpdateSecurityStampAsync(user!)).Succeeded.Should().BeTrue();
        await work.Commit(Token);
    }

    /// <summary>
    /// Runs the real filter around an endpoint that adds a secret and then returns a union of two result types, as
    /// a minimal API endpoint with two outcomes does. Returns how many secrets the space holds afterwards.
    /// </summary>
    private async Task<int> RunThroughFilter(
        (string Email, string Password, Guid UserId, Guid SpaceId) person,
        bool success)
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider;
            provider.GetRequiredService<TenantContext>().Set(person.SpaceId, person.UserId);
            var filter = new TenantScopeFilter(provider.GetRequiredService<TenantScope>());
            var context = new DefaultEndpointFilterInvocationContext(new DefaultHttpContext { RequestServices = provider });

            await filter.InvokeAsync(context, async _ =>
            {
                var db = provider.GetRequiredService<DroverDbContext>();
                db.Secrets.Add(new Secret
                {
                    SpaceId = person.SpaceId,
                    Kind = SecretKind.ModelKey,
                    Ciphertext = [1],
                    CreatedByUserId = person.UserId,
                });
                await db.SaveChangesAsync(Token);
                return Outcome(success);
            });
        }

        await using var reader = Services.CreateAsyncScope();
        reader.ServiceProvider.GetRequiredService<TenantContext>().Set(person.SpaceId, person.UserId);
        await using var work = await reader.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        return await reader.ServiceProvider.GetRequiredService<DroverDbContext>().Secrets.CountAsync(Token);
    }

    private static Results<Ok<string>, BadRequest> Outcome(bool success) =>
        success ? TypedResults.Ok("done") : TypedResults.BadRequest();

    #endregion
}
