namespace Drover.Client.Tests.Transport;

public sealed class NativeSessionRecoveryTests
{
    #region Public Methods

    [Fact]
    public async Task A_rejected_access_token_is_renewed_before_the_session_check_is_retried()
    {
        var storage = new SavedSession();
        var original = Tokens();
        var renewed = Tokens();
        using var native = Open(storage);
        using var client = Connect(native,
            Refresh(storage.Value, original), Session(original, HttpStatusCode.Unauthorized),
            Refresh(original.RefreshToken, renewed), Session(renewed, HttpStatusCode.OK));

        (await CheckSession(client)).Should().Be(HttpStatusCode.OK);
        storage.Value.Should().Be(renewed.RefreshToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.OK)] // A proxy can send a success without issuing credentials.
    public async Task An_unusable_refresh_response_leaves_the_credential_for_the_next_attempt(HttpStatusCode status)
    {
        var storage = new SavedSession();
        var original = storage.Value;
        using (var native = Open(storage))
        using (var client = Connect(native, request =>
            {
                ExpectRefresh(request);
                return new HttpResponseMessage(status) { Content = JsonContent.Create(new { }) };
            }))
        {
            var check = () => CheckSession(client);
            await check.Should().ThrowAsync<HttpRequestException>();
            storage.Value.Should().Be(original);
        }

        var renewed = Tokens();
        using var restarted = Open(storage);
        using var reopened = Connect(restarted, Refresh(original, renewed), Session(renewed, HttpStatusCode.OK));
        (await CheckSession(reopened)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_refresh_rejection_forgets_the_revoked_session()
    {
        var storage = new SavedSession();
        var issued = Tokens();
        using var native = Open(storage);
        using var client = Connect(native,
            Refresh(storage.Value, issued), Session(issued, HttpStatusCode.Unauthorized),
            request =>
            {
                ExpectRefresh(request);
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            },
            request =>
            {
                request.RequestUri!.AbsolutePath.Should().Be("/api/session");
                request.Headers.Authorization.Should().BeNull();
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            });

        (await CheckSession(client)).Should().Be(HttpStatusCode.Unauthorized);
        storage.Value.Should().BeNull();
        (await CheckSession(client)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_second_rejected_access_token_keeps_the_successfully_renewed_credential()
    {
        var storage = new SavedSession();
        var original = Tokens();
        var renewed = Tokens();
        var recovered = Tokens();
        using var native = Open(storage);
        using var client = Connect(native,
            Refresh(storage.Value, original), Session(original, HttpStatusCode.Unauthorized),
            Refresh(original.RefreshToken, renewed), Session(renewed, HttpStatusCode.Unauthorized),
            Session(renewed, HttpStatusCode.Unauthorized), Refresh(renewed.RefreshToken, recovered),
            Session(recovered, HttpStatusCode.OK));

        var check = () => CheckSession(client);
        await check.Should().ThrowAsync<HttpRequestException>();
        storage.Value.Should().Be(renewed.RefreshToken);
        (await CheckSession(client)).Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Private Methods

    private static NativeSession Open(SavedSession storage) =>
        new(storage, TimeProvider.System, NullLogger<NativeSession>.Instance);

    private static HttpMessageInvoker Connect(NativeSession session, params Func<HttpRequestMessage, HttpResponseMessage>[] answers)
    {
        var server = new ServerAddress();
        server.Set(new Uri("https://drover.example.test/"));
        return new HttpMessageInvoker(new AppRequests(server, session, new ScriptedTransport(answers)));
    }

    private static async Task<HttpStatusCode> CheckSession(HttpMessageInvoker client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(AppRequests.Placeholder, "api/session"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private static NativeSessionTokens Tokens() => new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), 600);

    private static void ExpectRefresh(HttpRequestMessage request)
    {
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be("/api/native/refresh");
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Refresh(string? previous, NativeSessionTokens issued) => request =>
    {
        ExpectRefresh(request);
        using var payload = JsonDocument.Parse(request.Content!.ReadAsStream());
        payload.RootElement.GetProperty("refreshToken").GetString().Should().Be(previous);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(issued) };
    };

    private static Func<HttpRequestMessage, HttpResponseMessage> Session(NativeSessionTokens issued, HttpStatusCode status) => request =>
    {
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.AbsolutePath.Should().Be("/api/session");
        request.Headers.Authorization!.Parameter.Should().Be(issued.AccessToken);
        return new HttpResponseMessage(status);
    };

    private sealed class ScriptedTransport(Func<HttpRequestMessage, HttpResponseMessage>[] answers) : HttpMessageHandler
    {
        #region Private Fields

        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> remaining = new(answers);

        #endregion

        #region Interface Implementations

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(remaining.Dequeue()(request));

        #endregion
    }

    private sealed class SavedSession : ISecureStorage
    {
        #region Properties

        public string? Value { get; private set; } = Guid.NewGuid().ToString("N");

        #endregion

        #region Interface Implementations

#pragma warning disable DRV0001 // Matches the storage API used by the linked transport.
        public Task<string?> GetAsync(string key) => Task.FromResult(Value);

        public Task SetAsync(string key, string value)
        {
            Value = value;
            return Task.CompletedTask;
        }
#pragma warning restore DRV0001

        public bool Remove(string key)
        {
            Value = null;
            return true;
        }

        #endregion
    }

    #endregion
}
