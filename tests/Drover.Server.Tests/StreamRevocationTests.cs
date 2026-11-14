namespace Drover.Server.Tests;

/// <summary>
/// An open event stream belongs to a session. When the session ends on the server, the stream ends too, and
/// nothing published after the end reaches it, whether the end was signalled in this server or only found by the
/// stream's own check.
/// </summary>
public sealed class StreamRevocationTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(10);

    private static readonly ConditionalWeakTable<HttpResponseMessage, StreamReader> readers = [];

    #endregion

    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Drover:SignInRateLimit:PermitLimit"] = "1000",
        ["Runs:HeartbeatSeconds"] = "0.2",
    };

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_stream_opened_with_a_copied_cookie_ends_when_the_owner_signs_out()
    {
        var person = await SeedPerson("alice");
        var owner = NewClient();
        await owner.SignIn(person.Email, person.Password);
        var thief = NewClient();
        thief.TakeCookiesOf(owner);
        using var stream = await OpenSignals(thief, person.SpaceId);
        await PublishAndSee(stream, person, "before");

        (await owner.Post("/api/sign-out")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        Publish(person, "after");

        var rest = await ReadToEnd(stream);
        rest.Should().NotContain("after");
    }

    [Fact]
    public async Task Changing_the_password_ends_the_streams_of_other_sessions_and_keeps_this_ones()
    {
        var person = await SeedPerson("alice");
        var laptop = NewClient();
        var phone = NewClient();
        await laptop.SignIn(person.Email, person.Password);
        await phone.SignIn(person.Email, person.Password);
        using var laptopStream = await OpenSignals(laptop, person.SpaceId);
        using var phoneStream = await OpenSignals(phone, person.SpaceId);
        await PublishAndSee(laptopStream, person, "first");
        await ReadUntil(phoneStream, "first");

        var changed = await laptop.Post(
            "/api/account/password",
            new ChangePasswordRequest(person.Password, NewPassword()));
        changed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Publish(person, "second");

        (await ReadToEnd(phoneStream)).Should().NotContain("second");
        await ReadUntil(laptopStream, "second");
    }

    [Fact]
    public async Task Redeeming_a_reset_code_ends_every_open_stream_of_that_person()
    {
        var person = await SeedPerson("alice");
        var laptop = NewClient();
        await laptop.SignIn(person.Email, person.Password);
        using var stream = await OpenSignals(laptop, person.SpaceId);
        await PublishAndSee(stream, person, "before");
        string code;
        await using (var scope = Services.CreateAsyncScope())
        {
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();
            code = (await passwords.IssueResetCode(person.Email, Token))!;
        }

        var reset = await NewClient().Post(
            "/api/password-reset",
            new ResetPasswordRequest(person.Email, code, NewPassword()));
        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Publish(person, "after");

        (await ReadToEnd(stream)).Should().NotContain("after");
    }

    [Fact]
    public async Task A_session_ended_where_this_server_was_not_told_ends_the_stream_at_its_next_check()
    {
        var person = await SeedPerson("alice");
        var laptop = NewClient();
        await laptop.SignIn(person.Email, person.Password);
        using var stream = await OpenSignals(laptop, person.SpaceId);
        await PublishAndSee(stream, person, "before");

        // As another server would: the row goes, and this server's revocation hub hears nothing.
        await RemoveSessionsQuietly(person.UserId);
        Time.Advance(TimeSpan.FromSeconds(16));
        Publish(person, "after");

        (await ReadToEnd(stream)).Should().NotContain("after");
    }

    #endregion

    #region Private Methods

    private static async Task<HttpResponseMessage> OpenSignals(ApiClient client, Guid spaceId)
    {
        var response = await client.Open($"/api/spaces/{spaceId}/stream");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return response;
    }

    private void Publish((string Email, string Password, Guid UserId, Guid SpaceId) person, string marker) =>
        Services.GetRequiredService<UserSignalHub>()
            .Publish(person.SpaceId, person.UserId, new UserSignal($"test.{marker}", null, null));

    /// <summary>Publishes until the stream shows the marker; a stream subscribes a moment after it opens.</summary>
    private async Task PublishAndSee(
        HttpResponseMessage stream,
        (string Email, string Password, Guid UserId, Guid SpaceId) person,
        string marker)
    {
        var reading = ReadUntil(stream, marker);

        while (!reading.IsCompleted)
        {
            Publish(person, marker);
            await Task.WhenAny(reading, Task.Delay(50, Token));
        }

        await reading;
    }

    private static async Task ReadUntil(HttpResponseMessage stream, string marker)
    {
        var reader = Reader(stream);
        var watch = Stopwatch.StartNew();

        while (true)
        {
            (watch.Elapsed < patience).Should().BeTrue($"the stream should show {marker}");
            var line = await reader.ReadLineAsync(Token).AsTask().WaitAsync(patience, Token);
            line.Should().NotBeNull($"the stream should stay open until it shows {marker}");

            if (line!.Contains(marker, StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    /// <summary>Reads until the server closes the stream, failing if it is still open when patience runs out.</summary>
    private static async Task<string> ReadToEnd(HttpResponseMessage stream)
    {
        var reader = Reader(stream);
        var text = new StringBuilder();
        var watch = Stopwatch.StartNew();

        while (await reader.ReadLineAsync(Token).AsTask().WaitAsync(patience, Token) is { } line)
        {
            text.Append(line).Append('\n');
            (watch.Elapsed < patience).Should().BeTrue("the server should close the stream of an ended session");
        }

        return text.ToString();
    }

    private static StreamReader Reader(HttpResponseMessage stream) =>
        readers.GetValue(stream, response => new StreamReader(response.Content.ReadAsStream(Token)));

    private async Task RemoveSessionsQuietly(Guid userId)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetUser(userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        await scope.ServiceProvider.GetRequiredService<DroverDbContext>().Database
            .ExecuteSqlAsync($"DELETE FROM user_sessions", Token);
        await work.Commit(Token);
    }

    #endregion
}
