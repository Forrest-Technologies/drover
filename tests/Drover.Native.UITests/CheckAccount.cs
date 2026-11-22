namespace Drover.Native.UITests;

/// <summary>
/// The account and the bot the app signs in to and talks to. They are made through the API, as the setup screens
/// make them, over a connection that trusts the run's certificate.
/// </summary>
internal sealed class CheckAccount
{
    #region Private Fields

    private readonly Uri origin;

    private readonly X509Certificate2 certificate;

    #endregion

    #region Constructors

    private CheckAccount(string email, string password, Uri origin, X509Certificate2 certificate)
    {
        Email = email;
        Password = password;
        this.origin = origin;
        this.certificate = certificate;
    }

    #endregion

    #region Properties

    public string Email { get; }

    public string Password { get; }

    /// <summary>The bot the app talks to, and the space it is in.</summary>
    public Guid BotId { get; private set; }

    public Guid SpaceId { get; private set; }

    #endregion

    #region Public Methods

    /// <summary>
    /// Makes the account and its bot. A <paramref name="desktop"/> bot also has a sandbox with a screen, started, and a
    /// model that reads pictures, for the Android proof.
    /// </summary>
    public static async Task<CheckAccount> Create(CheckServer server, X509Certificate2 certificate, bool desktop = false)
    {
        var account = new CheckAccount("screen-checks@example.test", $"pw-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}", server.Origin, certificate);
        var cookies = new CookieContainer();
        using var http = Connect(server.Origin, certificate, cookies);
        var session = await Send<SessionResponse>(http, cookies, "/api/setup",
            new SetupRequest(account.Email, "Screen checks", account.Password, server.SetupCode));
        var space = $"/api/spaces/{session.Spaces[0].Id}";
        var connection = await Send<ModelConnectionSummary>(http, cookies, $"{space}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted"));
        var bot = await Send<BotSummary>(http, cookies, $"{space}/bots", new CreateBotRequest("Check bot", "Answer briefly.", connection.Id, null));
        account.SpaceId = session.Spaces[0].Id;
        account.BotId = bot.Id;

        if (desktop)
        {
            await MarkAsReadingPictures(server, session, connection.Id);
            await Put(http, cookies, $"{space}/bots/{bot.Id}/definition",
                new UpdateBotRequest(null, null, null, null, null, null, null, null, null, "private", new SandboxSettingsBody("off", [], null, Desktop: true), null, null));
            // The first request for the screen starts the sandbox, so the app finds it running.
            await Send<object>(http, cookies, $"{space}/bots/{bot.Id}/sandbox/screen", null);
        }

        return account;
    }

    /// <summary>The bytes the server stored for the file the bot's conversations hold under this name, or null.</summary>
    public async Task<byte[]?> StoredFile(string name)
    {
        var cookies = new CookieContainer();
        using var http = Connect(origin, certificate, cookies);
        await Send<SessionResponse>(http, cookies, "/api/sign-in", new SignInRequest(Email, Password));
        var space = $"/api/spaces/{SpaceId}";
        var files = await http.GetFromJsonAsync<List<ExchangedFileResponse>>($"{space}/bots/{BotId}/files")
            ?? throw new InvalidOperationException("The server listed no files.");

        if (files.FirstOrDefault(exchanged => exchanged.File.Name == name) is not { } found)
        {
            return null;
        }

        return await http.GetByteArrayAsync($"{space}/files/{found.File.Id}");
    }

    #endregion

    #region Private Methods

    // The harness talks only to its own test server, with a cookie jar as a browser keeps one.
    private static HttpClient Connect(Uri origin, X509Certificate2 certificate, CookieContainer cookies)
    {
        var hash = TestCertificate.Hash(certificate);
        var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
                string.Equals(presented?.GetCertHashString(HashAlgorithmName.SHA256), hash, StringComparison.OrdinalIgnoreCase),
        };
        return new HttpClient(handler) { BaseAddress = origin };
    }

    private static async Task Put(HttpClient http, CookieContainer cookies, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body, body.GetType()) };
        request.Headers.Add("X-XSRF-TOKEN", await Token(http, cookies));
        var response = await http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"PUT {path} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    // A picture has nowhere to go when the bot's model cannot read pictures, so the check bot's model is marked as one
    // that can. The API takes this mark only for a compatible endpoint, and the check bot's model is a scripted one, so
    // the catalog row is written directly. DRV0008 exempts assemblies named ".Tests", and this harness is not one.
#pragma warning disable DRV0008
    private static async Task MarkAsReadingPictures(CheckServer server, SessionResponse session, Guid connectionId)
    {
        await using var scope = server.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(session.Spaces[0].Id, session.UserId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(CancellationToken.None);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        db.ModelCatalog.Add(new ModelCatalogEntry
        {
            ModelConnectionId = connectionId,
            Model = "scripted",
            DisplayName = "scripted",
            Capabilities = new ModelCapabilities { Vision = true, VisionSetByOperator = true },
            LastSeenAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        await work.Commit(CancellationToken.None);
    }
#pragma warning restore DRV0008

    private static async Task<T> Send<T>(HttpClient http, CookieContainer cookies, string path, object? body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        request.Headers.Add("X-XSRF-TOKEN", await Token(http, cookies));
        var response = await http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"POST {path} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return typeof(T) == typeof(object)
            ? default!
            : await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException($"POST {path} returned no body.");
    }

    private static async Task<string> Token(HttpClient http, CookieContainer cookies)
    {
        (await http.GetAsync(new Uri("/api/antiforgery", UriKind.Relative))).EnsureSuccessStatusCode();
        return cookies.GetAllCookies().FirstOrDefault(cookie => cookie.Name == "XSRF-TOKEN")?.Value
            ?? throw new InvalidOperationException("The server set no antiforgery cookie.");
    }

    #endregion
}
