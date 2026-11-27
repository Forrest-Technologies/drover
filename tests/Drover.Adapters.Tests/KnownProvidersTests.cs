namespace Drover.Adapters.Tests;

/// <summary>The table of named providers: what it holds, and what a connection made from a row sends and to where.</summary>
public sealed class KnownProvidersTests
{
    #region Private Fields

    private static readonly string[] lead = ["openai", "openrouter", "gemini", "xai", "groq", "deepseek", "mistral"];

    /// <summary>A value each row with a blank accepts.</summary>
    private static readonly Dictionary<string, string> samples = new()
    {
        ["alibaba"] = "ws-1a2b3c",
        ["bedrock"] = "us-east-1",
        ["cloudflare-workers-ai"] = "0123456789abcdef0123456789abcdef",
        ["databricks"] = "dbc-1234.cloud.databricks.com",
        ["snowflake"] = "myorg-myaccount",
    };

    /// <summary>Values that would end the host or start a path, a user, a port, a query or a fragment.</summary>
    private static readonly string[] hostile =
        ["x/evil.test", "evil.test/x", "x@evil.test", "x:80", "x?y=1", "x#y", "x y", "x\ty", "x\ny", "", "..", "x..y", ".x", "x."];

    #endregion

    #region Properties

    public static TheoryData<string> Ids => [.. KnownProviders.All.Select(row => row.Id)];

    #endregion

    #region Table

    [Fact]
    public void Every_row_has_a_unique_id_and_a_safe_address()
    {
        KnownProviders.All.Select(row => row.Id).Should().OnlyHaveUniqueItems();
        KnownProviders.All.Should().OnlyContain(row => row.Id.Length > 0
            && row.Id.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-'));
        KnownProviders.All.Should().OnlyContain(row => row.DisplayName.Trim().Length > 0);

        foreach (var row in KnownProviders.All)
        {
            var address = new Uri(Filled(row), UriKind.Absolute);

            address.Scheme.Should().Be(row.Local ? address.Scheme : Uri.UriSchemeHttps, row.Id);
            address.Scheme.Should().BeOneOf(Uri.UriSchemeHttp, Uri.UriSchemeHttps);
            address.UserInfo.Should().BeEmpty(row.Id);
            address.Query.Should().BeEmpty(row.Id);
            address.Fragment.Should().BeEmpty(row.Id);

            if (row.KeyPage is not null)
            {
                new Uri(row.KeyPage, UriKind.Absolute).Scheme.Should().Be(Uri.UriSchemeHttps, row.Id);
            }
        }
    }

    [Fact]
    public void A_local_row_needs_no_key()
    {
        var local = KnownProviders.All.Where(row => row.Local).ToList();

        local.Should().NotBeEmpty();
        local.Should().OnlyContain(row => row.KeyRule != ModelProviderKeyRule.Required);
        local.Select(row => row.KeyPage).Should().OnlyContain(page => page == null);
        KnownProviders.All.Where(row => !row.Local).Should().OnlyContain(row => row.KeyRule == ModelProviderKeyRule.Required);
    }

    [Fact]
    public void The_rows_come_in_the_order_a_person_expects()
    {
        var ids = KnownProviders.All.Select(row => row.Id).ToList();
        var hosted = KnownProviders.All.Where(row => !row.Local && !lead.Contains(row.Id)).Select(row => row.DisplayName).ToList();

        ids.Take(lead.Length).Should().Equal(lead);
        hosted.Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        KnownProviders.All.SkipWhile(row => !row.Local).Should().OnlyContain(row => row.Local, "local rows come last");
        KnownProviders.All.TakeWhile(row => !row.Local).Should().OnlyContain(row => !row.Local);
    }

    [Fact]
    public void The_provider_offers_the_table_in_the_same_order()
    {
        new OpenAICompatibleProvider(new FixtureHttpClientFactory(new ScriptedHandler((_, _, _) => throw new InvalidOperationException())))
            .Descriptor.NamedProviders.Should().Equal(KnownProviders.All);
    }

    [Fact]
    public void Every_blank_has_one_marker_a_label_and_a_sample()
    {
        var blanks = KnownProviders.All.Where(row => row.AddressBlank is not null).ToList();

        blanks.Select(row => row.Id).Should().BeEquivalentTo(samples.Keys);
        blanks.Should().OnlyContain(row => row.AddressBlank!.Label.Trim().Length > 0
            && row.Address.Split(AddressBlanks.Marker).Length == 2 && !row.Local);
        KnownProviders.All.Where(row => row.AddressBlank is null).Should().OnlyContain(row => !row.Address.Contains(AddressBlanks.Marker));
    }

    [Fact]
    public void A_blank_cannot_change_the_host_or_the_path()
    {
        foreach (var row in KnownProviders.All.Where(row => row.AddressBlank is not null))
        {
            var template = new Uri(row.Address.Replace(AddressBlanks.Marker, samples[row.Id]), UriKind.Absolute);
            var built = new Uri(row.AddressFor(samples[row.Id])!, UriKind.Absolute);

            // The value fills its place and changes nothing around it.
            built.AbsolutePath.Should().Be(template.AbsolutePath, row.Id);
            built.Scheme.Should().Be(Uri.UriSchemeHttps, row.Id);
            built.UserInfo.Should().BeEmpty(row.Id);
            built.Query.Should().BeEmpty(row.Id);
            built.Fragment.Should().BeEmpty(row.Id);
            built.IsDefaultPort.Should().BeTrue(row.Id);

            var parts = row.Address.Split(AddressBlanks.Marker);
            var inHost = !parts[0]["https://".Length..].Contains('/');

            if (inHost)
            {
                // A blank inside the host fills its place and leaves the rest of the host as written.
                built.Host.Should().EndWith(parts[1].Split('/')[0], row.Id);
                built.Host.Should().StartWith(parts[0]["https://".Length..], row.Id);
            }
            else
            {
                built.Host.Should().Be(template.Host, row.Id);
            }

            if (row.Id == "alibaba")
            {
                row.AddressFor("-x").Should().BeNull();
                row.AddressFor("x-").Should().BeNull();
            }

            if (row.Id is "databricks" or "snowflake")
            {
                row.AddressFor(string.Join('.', Enumerable.Repeat("a", 127))).Should().NotBeNull(row.Id);
                row.AddressFor(string.Join('.', Enumerable.Repeat("a", 128))).Should().BeNull($"{row.Id} caps a value at 253 characters");
            }

            foreach (var value in hostile)
            {
                row.AddressFor(value).Should().BeNull($"{row.Id} must refuse '{value}'");
            }

            foreach (var character in "/@:?# \t\r\n")
            {
                row.AddressFor($"{samples[row.Id]}{character}x").Should().BeNull($"{row.Id} must refuse {(int)character}");
                row.AddressFor($"{character}{samples[row.Id]}").Should().BeNull($"{row.Id} must refuse {(int)character}");
            }
        }
    }

    #endregion

    #region Wire

    [Theory]
    [MemberData(nameof(Ids))]
    public async Task Every_row_sends_chat_to_its_own_address_with_a_bearer_key(string id)
    {
        var row = KnownProviders.All.Single(candidate => candidate.Id == id);
        List<HttpRequestMessage> sent = [];
        var handler = new ScriptedHandler((request, _, _) =>
        {
            sent.Add(request);
            return Task.FromResult(ScriptedHandler.Json(HttpStatusCode.OK, ScriptedHandler.ChatReply));
        });
        var provider = new OpenAICompatibleProvider(new FixtureHttpClientFactory(handler));
        var key = TestKeys.Generate();
        var filled = Filled(row);
        var endpoint = new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, new Uri(filled));

        await provider.CreateChatClient(endpoint, key, "any-model", new ModelSettings()).Ask("Say hello.");
        var models = await provider.ListModels(endpoint, key, TestContext.Current.CancellationToken);

        var expectedBase = filled.TrimEnd('/');
        sent.Should().HaveCount(2);
        sent.Select(request => request.RequestUri!.GetLeftPart(UriPartial.Path))
            .Should().BeEquivalentTo([$"{expectedBase}/chat/completions", $"{expectedBase}/models"]);
        sent.Should().OnlyContain(request => request.RequestUri!.Host == new Uri(filled).Host);
        sent.Should().OnlyContain(request => request.Headers.Authorization!.Scheme == "Bearer" && request.Headers.Authorization.Parameter == key);
        models.Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    /// <summary>The row's address, with a value the row's blank accepts where it has one.</summary>
    private static string Filled(ModelProviderPreset row) => row.AddressBlank is null ? row.Address : row.AddressFor(samples[row.Id])!;

    #endregion
}
