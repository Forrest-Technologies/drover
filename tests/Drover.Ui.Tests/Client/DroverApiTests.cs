namespace Drover.Ui.Tests.Client;

public sealed class DroverApiTests : IDisposable
{
    #region Private Fields

    private readonly FakeServer server = new();

    #endregion

    #region Public Methods

    public void Dispose() => server.Dispose();

    [Fact]
    public async Task Nobody_signed_in_is_an_answer_not_a_failure()
    {
        server.Answer("GET", "/api/session", HttpStatusCode.Unauthorized);

        (await server.CreateApi().GetSession(Xunit.TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task Setup_that_is_closed_is_an_answer_not_a_failure()
    {
        (await server.CreateApi().GetSetupStatus(Xunit.TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task A_refusal_carries_the_servers_own_sentence()
    {
        server.Refuse("POST", "/api/sign-in", HttpStatusCode.Unauthorized, "The email or password is not right.");

        var signIn = () => server.CreateApi().SignIn(new("sam@example.org", "wrong"), Xunit.TestContext.Current.CancellationToken);

        var refusal = (await signIn.Should().ThrowAsync<DroverApiException>()).Which;
        refusal.Message.Should().Be("The email or password is not right.");
        refusal.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "Too many attempts. Wait a minute and try again.")]
    [InlineData(HttpStatusCode.InternalServerError, "The server failed to answer. Try again, and check the server log if it keeps happening.")]
    [InlineData(HttpStatusCode.Forbidden, "Your account is not allowed to do that.")]
    public async Task A_refusal_with_no_sentence_is_shown_with_one_for_its_status(HttpStatusCode status, string sentence)
    {
        server.Answer("POST", "/api/sign-in", status);

        var signIn = () => server.CreateApi().SignIn(new("sam@example.org", "secret"), Xunit.TestContext.Current.CancellationToken);

        var refusal = (await signIn.Should().ThrowAsync<DroverApiException>()).Which;
        refusal.StatusCode.Should().Be(status);
        Failures.Sentence(refusal).Should().Be(sentence);
    }

    [Fact]
    public void A_server_that_cannot_be_reached_is_said_in_words()
    {
        Failures.Sentence(new HttpRequestException("connection refused")).Should().Be(Failures.Unreachable);
    }

    [Fact]
    public async Task A_model_connection_is_created_and_tested_under_its_space()
    {
        var spaceId = Guid.NewGuid();
        var connection = new ModelConnectionSummary(Guid.NewGuid(), "Venice", "venice", null, true, "untested", null, [], null, null);
        server.Answer("POST", $"/api/spaces/{spaceId}/model-connections", connection, HttpStatusCode.Created);
        server.Answer(
            "POST",
            $"/api/spaces/{spaceId}/model-connections/{connection.Id}/test",
            new ConnectionTestResponse(true, "Connected.", null, connection));
        var api = server.CreateApi();

        var created = await api.CreateModelConnection(spaceId, new("venice", "Venice", null, "sample-key", null), Xunit.TestContext.Current.CancellationToken);
        var tested = await api.TestModelConnection(spaceId, created.Id, Xunit.TestContext.Current.CancellationToken);

        created.Id.Should().Be(connection.Id);
        tested.Succeeded.Should().BeTrue();
        tested.Message.Should().Be("Connected.");
    }

    #endregion
}
