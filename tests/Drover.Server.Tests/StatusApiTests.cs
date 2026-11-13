namespace Drover.Server.Tests;

/// <summary>
/// The status page of a space, as its owner sees it on a self-hosted server: the server's own rows first, then one
/// row for each connection of the space, from what the connection last reported.
/// </summary>
public sealed class StatusApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Public Methods

    [Fact]
    public async Task The_owner_sees_the_server_rows_and_each_is_green_on_a_fresh_server_but_the_public_address()
    {
        var (client, session, _) = await SetUpFirstAccount();

        var report = await Status(client, session.Spaces[0].Id);

        report.ServerRowsShown.Should().BeTrue();
        report.Rows.Where(row => row.Area == "server").Select(row => (row.Name, row.State)).Should().Equal(
            ("Database", "ok"),
            ("Migrations", "ok"),
            ("Key ring", "ok"),
            ("Public address", "warning"),
            ("Background work", "ok"));
        Row(report, "Database").Summary.Should().Contain("drover_app");
        Row(report, "Public address").Summary.Should().Contain(PublicAddress.Variable);
    }

    [Fact]
    public async Task Each_model_connection_of_the_space_is_a_row_with_its_last_result_and_no_key()
    {
        var (client, session, _) = await SetUpFirstAccount();
        var checkedAt = DateTimeOffset.UtcNow.AddMinutes(-3);
        await SeedModelConnection(session.Spaces[0].Id, session.UserId, "Working", ConnectionStatus.Ready, checkedAt);
        await SeedModelConnection(session.Spaces[0].Id, session.UserId, "Broken", ConnectionStatus.Failed, checkedAt);

        var report = await Status(client, session.Spaces[0].Id);

        Row(report, "Working").Should().Match<StatusRow>(row => row.Area == "space" && row.State == "ok" && row.CheckedAt != null);
        Row(report, "Broken").State.Should().Be("problem");
    }

    [Fact]
    public async Task Another_space_s_connections_never_appear()
    {
        var (client, session, _) = await SetUpFirstAccount();
        var (_, _, otherUser, otherSpace) = await SeedPerson("neighbour");
        await SeedModelConnection(otherSpace, otherUser, "Neighbour's model", ConnectionStatus.Ready, DateTimeOffset.UtcNow);

        var report = await Status(client, session.Spaces[0].Id);

        report.Rows.Should().NotContain(row => row.Name == "Neighbour's model");
    }

    [Fact]
    public async Task A_member_who_is_not_an_admin_is_refused()
    {
        var (_, session, _) = await SetUpFirstAccount();
        var (email, password, _) = await SeedMember(session.Spaces[0].Id, "member", MembershipRole.Member);
        var member = NewClient();
        (await member.SignIn(email, password)).EnsureSuccessStatusCode();

        var response = await member.Get($"/api/spaces/{session.Spaces[0].Id}/status");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_stranger_to_the_space_is_told_it_does_not_exist()
    {
        var (_, session, _) = await SetUpFirstAccount();
        var (email, password, _, _) = await SeedPerson("stranger");
        var stranger = NewClient();
        (await stranger.SignIn(email, password)).EnsureSuccessStatusCode();

        var response = await stranger.Get($"/api/spaces/{session.Spaces[0].Id}/status");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Private Methods

    private static StatusRow Row(StatusReport report, string name) => report.Rows.Single(row => row.Name == name);

    private static async Task<StatusReport> Status(ApiClient client, Guid spaceId)
    {
        var response = await client.Get($"/api/spaces/{spaceId}/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<StatusReport>(Token))!;
    }

    private async Task SeedModelConnection(Guid spaceId, Guid userId, string name, ConnectionStatus status, DateTimeOffset checkedAt)
    {
        await using var scope = Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await provider.GetRequiredService<TenantScope>().Begin(Token);
        provider.GetRequiredService<DroverDbContext>().ModelConnections.Add(new ModelConnection
        {
            SpaceId = spaceId,
            Name = name,
            Kind = "openai-compatible",
            BaseUrl = "https://models.example.test/v1",
            Status = status,
            LastCheckedAt = checkedAt,
        });
        await work.Commit(Token);
    }

    #endregion
}

/// <summary>The public address, from the variable a compose file passes through, and from the setting.</summary>
public sealed class PublicAddressTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        [PublicAddress.Variable] = "https://drover.example.test",
    };

    #endregion

    #region Public Methods

    [Fact]
    public async Task DROVER_PUBLIC_URL_sets_where_sign_ins_return_and_the_status_row_is_green()
    {
        var (client, session, _) = await SetUpFirstAccount();

        var report = (await (await client.Get($"/api/spaces/{session.Spaces[0].Id}/status")).Content.ReadFromJsonAsync<StatusReport>(Token))!;

        Services.GetRequiredService<IOptions<ToolSignInOptions>>().Value.RedirectUrl
            .Should().Be(new Uri("https://drover.example.test/oauth/mcp/callback"));
        report.Rows.Single(row => row.Name == "Public address").State.Should().Be("ok");
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData("not an address", null, null)]
    [InlineData("", "https://setting.example.test", "https://setting.example.test/")]
    [InlineData("https://variable.example.test", "https://setting.example.test", "https://variable.example.test/")]
    public void The_variable_wins_and_an_empty_or_unusable_value_counts_as_not_set(string variable, string? setting, string? expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PublicAddress.Variable] = variable,
            [PublicAddress.Setting] = setting,
        }).Build();

        var address = PublicAddress.Read(configuration);

        (address?.ToString()).Should().Be(expected);
    }

    [Fact]
    public void The_setup_address_is_the_public_one_or_each_listening_address_as_this_machine()
    {
        SetupCodeAnnouncer.SetupAddresses(new Uri("https://drover.example.test/any"), ["http://[::]:8080"])
            .Should().Equal("https://drover.example.test/setup");
        SetupCodeAnnouncer.SetupAddresses(null, ["http://[::]:8080", "http://+:8080", "http://127.0.0.1:5000/"])
            .Should().Equal("http://localhost:8080/setup", "http://127.0.0.1:5000/setup");
    }

    #endregion
}
