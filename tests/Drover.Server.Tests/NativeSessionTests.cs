namespace Drover.Server.Tests;

public sealed class NativeSessionTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Public Methods

    [Fact]
    public async Task A_native_session_requires_the_cookie_and_antiforgery_before_it_can_use_bearer_authentication()
    {
        var person = await SeedPerson("native-owner");
        var preview = NewClient();
        (await preview.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        (await preview.PostWithoutToken("/api/native/session")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await NewClient().Post("/api/native/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var tokens = await ReadTokens(await preview.Post("/api/native/session"));
        var native = NewClient();
        native.ForwardedHeaders["Authorization"] = $"Bearer {tokens.AccessToken}";

        var response = await native.Get("/api/session");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>(Token);
        session!.UserId.Should().Be(person.UserId);

        (await native.PostWithoutToken("/api/sign-out")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await native.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await preview.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await NewClient().PostWithoutToken("/api/native/refresh", new { tokens.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_cold_client_can_refresh_its_access_after_the_server_is_replaced_until_its_session_expires()
    {
        var person = await SeedPerson("native-refresh");
        var preview = NewClient();
        (await preview.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        var tokens = await ReadTokens(await preview.Post("/api/native/session"));

        await RestartServer();
        var native = NewClient();
        native.ForwardedHeaders["Authorization"] = $"Bearer {tokens.AccessToken}";
        (await native.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.OK);

        Time.Advance(TimeSpan.FromMinutes(11));
        (await native.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var renewed = await ReadTokens(await NewClient()
            .PostWithoutToken("/api/native/refresh", new { tokens.RefreshToken }));
        native.ForwardedHeaders["Authorization"] = $"Bearer {renewed.AccessToken}";
        (await native.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.OK);

        Time.Advance(TimeSpan.FromDays(15));
        (await NewClient().PostWithoutToken("/api/native/refresh", new { renewed.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Changing_the_password_replaces_the_native_credentials()
    {
        var person = await SeedPerson("native-password");
        var preview = NewClient();
        (await preview.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        var original = await ReadTokens(await preview.Post("/api/native/session"));

        var native = NewClient();
        native.ForwardedHeaders["Authorization"] = $"Bearer {original.AccessToken}";
        var nextPassword = Guid.NewGuid().ToString("N");
        (await native.PostWithoutToken("/api/account/password", new ChangePasswordRequest(person.Password, nextPassword)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await native.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await NewClient().PostWithoutToken("/api/native/refresh", new { original.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        native.ForwardedHeaders.Clear();
        var replacement = await ReadTokens(await native.Post("/api/native/session"));
        var restored = NewClient();
        restored.ForwardedHeaders["Authorization"] = $"Bearer {replacement.AccessToken}";
        (await restored.Get("/api/session")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Private Methods

    private async Task<Tokens> ReadTokens(HttpResponseMessage response)
    {
        using (response)
        {
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<Tokens>(Token))!;
        }
    }

    private sealed record Tokens(string AccessToken, string RefreshToken)
    {
        public override string ToString() => nameof(Tokens);
    }

    #endregion
}
