namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// The display start against the real E2B service with a fresh desktop sandbox: a bot's tool and a person looking at
/// the screen can both be first to use it, so the start runs twice at once, and both must find the display and the
/// desktop session running. The key is in DROVER_TEST_E2B_KEY, the domain in DROVER_TEST_E2B_DOMAIN (default e2b.app).
/// Explicit and marked Live, so the normal run and pull request CI never execute it. The sandbox is destroyed at the
/// end, pass or fail. Run it with:
/// <c>DROVER_TEST_E2B_KEY=... dotnet test --project tests/Drover.Engine.Tests -- --explicit only --filter-trait "Live=true" --filter-class Drover.Engine.Tests.Sandboxes.DisplayStartLiveTests</c>.
/// </summary>
[Trait("Live", "true")]
public sealed class DisplayStartLiveTests
{
    #region Private Fields

    private const string KeyVariable = "DROVER_TEST_E2B_KEY";

    private const string DomainVariable = "DROVER_TEST_E2B_DOMAIN";

    #endregion

    #region Public Methods

    [Fact(Explicit = true)]
    public async Task Two_starts_of_the_display_at_once_both_find_it_running()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddE2BSandboxProvider(options =>
            {
                options.ApiKey = Environment.GetEnvironmentVariable(KeyVariable)
                    ?? throw new InvalidOperationException($"Set {KeyVariable} to an E2B API key to run the live display test.");
                options.Domain = Environment.GetEnvironmentVariable(DomainVariable) ?? options.Domain;
            })
            .BuildServiceProvider();
        var provider = services.GetRequiredService<ISandboxProvider>();
        var sandbox = await provider.Create(
            new SandboxSpec
            {
                SpaceId = Guid.NewGuid(),
                SharingGroupId = Guid.NewGuid(),
                IdempotencyKey = Guid.NewGuid(),
                Network = new NetworkPolicy(NetworkAccess.Off, []),
                IdleTimeout = TimeSpan.FromMinutes(3),
                Desktop = true,
                Environment = DesktopCommands.Environment,
            },
            TestContext.Current.CancellationToken);

        try
        {
            var starts = await Task.WhenAll(Start(provider, sandbox), Start(provider, sandbox));

            starts.Should().Equal([0, 0], "the second start waits for the first and then finds the desktop running");
            (await Start(provider, sandbox)).Should().Be(0, "a start on a running desktop does nothing");
        }
        finally
        {
            await provider.Destroy(sandbox, CancellationToken.None);
        }
    }

    #endregion

    #region Private Methods

    private static async Task<int?> Start(ISandboxProvider provider, SandboxHandle sandbox)
    {
        int? exitCode = null;

        await foreach (var output in provider.Run(sandbox, DesktopCommands.StartDisplay(), TestContext.Current.CancellationToken))
        {
            if (output is CommandExited exited)
            {
                exitCode = exited.ExitCode;
            }
        }

        return exitCode;
    }

    #endregion
}
