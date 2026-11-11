namespace Drover.Domain.Tests;

/// <summary>
/// Outbound calls come from the client factory, so the policy handler is on every one. The build enforces the bans;
/// this keeps the entries from being deleted and the exemptions from growing unnoticed.
/// </summary>
public sealed class OutboundBansTests
{
    #region Public Methods

    [Theory]
    [InlineData("M:System.Net.Http.HttpClient.#ctor;")]
    [InlineData("M:System.Net.Http.HttpClient.#ctor(System.Net.Http.HttpMessageHandler);")]
    [InlineData("M:System.Net.Http.HttpClient.#ctor(System.Net.Http.HttpMessageHandler,System.Boolean);")]
    [InlineData("M:System.Net.Http.SocketsHttpHandler.#ctor;")]
    [InlineData("M:System.Net.Http.HttpClientHandler.#ctor;")]
    public void Building_an_outbound_client_or_handler_by_hand_is_banned(string entry)
    {
        File.ReadAllLines(Path.Combine(FindRepositoryRoot(), "BannedSymbols.txt"))
            .Should().Contain(line => line.StartsWith(entry, StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_policy_code_the_browser_client_and_the_handler_tests_are_exempt_from_the_ban()
    {
        var exempt = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), ".editorconfig"))
            .Select((line, index) => (line, index))
            .Where(entry => entry.line.StartsWith('['))
            .Where(entry => File.ReadAllLines(Path.Combine(FindRepositoryRoot(), ".editorconfig"))
                .Skip(entry.index + 1)
                .TakeWhile(next => !next.StartsWith('['))
                .Contains("dotnet_diagnostic.RS0030.severity = none"))
            .Select(entry => entry.line)
            .ToList();

        exempt.Should().BeEquivalentTo(
        [
            "[src/Drover.Engine/Policy/OutboundHttp.cs]",
            "[src/Drover.Web/Program.cs]",
            "[tests/Drover.Engine.Tests/OutboundHttpTests.cs]",
            "[**/obj/**/*.cs]",
        ]);
    }

    #endregion

    #region Private Methods

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Drover.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Drover.slnx was not found above the test directory.");
    }

    #endregion
}
