namespace Drover.Server.Tests;

public sealed class PreviewOriginTests
{
    #region Tests

    [Fact]
    public void No_preview_address_leaves_preview_off()
    {
        Drover.Server.Files.PreviewOrigin.Read(Configuration(new Dictionary<string, string?>())).Should().BeNull();
    }

    [Theory]
    [InlineData(null, "https://preview.other.test")]
    [InlineData("http://app.example.test", "https://preview.other.test")]
    [InlineData("https://app.example.test", "http://preview.other.test")]
    [InlineData("https://app.example.test", "https://preview.example.test")]
    [InlineData("https://app.example.test", "https://preview.other.test.")]
    [InlineData("https://app.example.test", "https://preview..other.test")]
    [InlineData("https://app.example.test", "https://preview.other.test/path")]
    [InlineData("https://app.example.test", "https://127.0.0.1")]
    public void Preview_requires_two_plain_https_origins_on_separate_sites(string? app, string preview)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            [PublicAddress.Variable] = app,
            [Drover.Server.Files.PreviewOrigin.Variable] = preview,
        });

        var read = () => Drover.Server.Files.PreviewOrigin.Read(configuration);

        read.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Preview_accepts_two_https_origins_on_separate_sites()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            [PublicAddress.Variable] = "https://app.example.test",
            [Drover.Server.Files.PreviewOrigin.Variable] = "https://preview.other.test",
        });

        Drover.Server.Files.PreviewOrigin.Read(configuration).Should().Be(
            new Drover.Server.Files.PreviewOrigin(
                new Uri("https://app.example.test"), new Uri("https://preview.other.test")));
    }

    [Fact]
    public async Task Preview_grant_can_be_taken_once_under_concurrency()
    {
        var grants = new Drover.Server.Files.PreviewGrantStore(TimeProvider.System);
        var token = grants.Add(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64));

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => grants.Take(token!))));

        results.Count(grant => grant is not null).Should().Be(1);
    }

    [Fact]
    public void Preview_grant_store_is_bounded_and_expired_entries_make_room()
    {
        var time = new AdjustableTimeProvider();
        var grants = new Drover.Server.Files.PreviewGrantStore(time);
        var spaceId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var fileId = Guid.CreateVersion7();

        var tokens = Enumerable.Range(0, 10_000)
            .Select(_ => grants.Add(spaceId, userId, fileId, new string('a', 64)))
            .ToArray();

        tokens.Count(token => token is not null).Should().Be(10_000);
        grants.Add(spaceId, userId, fileId, new string('a', 64)).Should().BeNull();

        time.Advance(TimeSpan.FromMinutes(2));
        grants.Add(spaceId, userId, fileId, new string('a', 64)).Should().NotBeNull();
    }

    #endregion

    #region Private Methods

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    #endregion
}
