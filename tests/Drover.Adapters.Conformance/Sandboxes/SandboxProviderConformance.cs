namespace Drover.Adapters.Conformance.Sandboxes;

/// <summary>
/// The suite every <see cref="ISandboxProvider"/> must pass. The scenarios are data, so one suite serves the offline
/// run, where an adapter's test class uses a plain theory, and the live run, where it marks the same theory explicit:
/// an inherited fact cannot be made explicit, and pull request CI does not filter by trait.
/// A new behavior every adapter must have is added here first.
/// </summary>
public abstract class SandboxProviderConformance
{
    #region Private Fields

    /// <summary>Several lines of output, a pause between them so they arrive as separate pieces, stderr, and a failure.</summary>
    public const string StreamingCommand = "for i in 1 2 3; do echo line$i; sleep 0.3; done; echo oops >&2; exit 3";

    /// <summary>Exits 0 once something answers on the public internet. curl is in every adapter's default image.</summary>
    public const string ReachOutCommand = "curl -sS -m 5 -o /dev/null https://example.com";

    /// <summary>Exits 1 when no sleep process is left, which is how a scenario sees that a command was killed.</summary>
    public const string NoSleepLeftCommand = "pgrep -x sleep";

    /// <summary>A NUL and bytes that are not UTF-8, so a text round trip would fail.</summary>
    public static readonly byte[] FileContent = [0x68, 0x65, 0x6c, 0x6c, 0x6f, 0x00, 0xff, 0x01];

    public static readonly CommandRequest LongSleep = new() { Program = "/bin/sleep", Arguments = ["30"] };

    #endregion

    #region Properties

    public static TheoryData<string> Scenarios { get; } =
    [
        SandboxScenarios.CreatesWithMetadata,
        SandboxScenarios.KeepsAlive,
        SandboxScenarios.RunsACommand,
        SandboxScenarios.ExitsZero,
        SandboxScenarios.RoundTripsBinary,
        SandboxScenarios.ListsFiles,
        SandboxScenarios.DeniesNetwork,
        SandboxScenarios.PausesAndResumes,
        SandboxScenarios.Destroys,
        SandboxScenarios.RefusesAnotherGroupsHandle,
        SandboxScenarios.RefusesAWrongToken,
        SandboxScenarios.KillsOnCancel,
        SandboxScenarios.StopsAtTheTimeLimit,
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    #endregion

    #region Adapter Hooks

    protected abstract SandboxUnderTest Start(string scenario);

    #endregion

    #region Scenario Runner

    protected Task RunScenario(string scenario)
    {
        var subject = Start(scenario);

        return scenario switch
        {
            SandboxScenarios.CreatesWithMetadata => CreatesWithMetadata(subject),
            SandboxScenarios.KeepsAlive => KeepsAlive(subject),
            SandboxScenarios.RunsACommand => RunsACommand(subject),
            SandboxScenarios.ExitsZero => ExitsZero(subject),
            SandboxScenarios.RoundTripsBinary => RoundTripsBinary(subject),
            SandboxScenarios.ListsFiles => ListsFiles(subject),
            SandboxScenarios.DeniesNetwork => DeniesNetwork(subject),
            SandboxScenarios.PausesAndResumes => PausesAndResumes(subject),
            SandboxScenarios.Destroys => Destroys(subject),
            SandboxScenarios.RefusesAnotherGroupsHandle => RefusesAnotherGroupsHandle(subject),
            SandboxScenarios.RefusesAWrongToken => RefusesAWrongToken(subject),
            SandboxScenarios.KillsOnCancel => KillsOnCancel(subject),
            SandboxScenarios.StopsAtTheTimeLimit => StopsAtTheTimeLimit(subject),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "No such sandbox scenario."),
        };
    }

    #endregion

    #region Lifecycle

    private static async Task CreatesWithMetadata(SandboxUnderTest subject)
    {
        var handle = await Create(subject);

        handle.ProviderSandboxId.Should().NotBeNullOrWhiteSpace();
        handle.AccessToken.Should().NotBeNullOrWhiteSpace();
        handle.SpaceId.Should().Be(subject.Spec.SpaceId);
        handle.SharingGroupId.Should().Be(subject.Spec.SharingGroupId);
        handle.ToString().Should().NotContain(handle.AccessToken, "a printed handle must never show the token");

        var info = await subject.Provider.Describe(handle, Cancellation);

        info.State.Should().Be(ProviderSandboxState.Running);
        info.Metadata.Should().Contain("drover.space", subject.Spec.SpaceId.ToString("D"));
        info.Metadata.Should().Contain("drover.group", subject.Spec.SharingGroupId.ToString("D"));
        ShouldBeDroverTypes(handle, info);
    }

    private static async Task KeepsAlive(SandboxUnderTest subject)
    {
        var handle = await Create(subject);
        var before = await subject.Provider.Describe(handle, Cancellation);

        await subject.Provider.KeepAlive(handle, subject.Spec.IdleTimeout + TimeSpan.FromMinutes(1), Cancellation);
        var after = await subject.Provider.Describe(handle, Cancellation);

        before.EndsAt.Should().NotBeNull();
        after.EndsAt.Should().BeAfter(before.EndsAt!.Value);
    }

    private static async Task PausesAndResumes(SandboxUnderTest subject)
    {
        var handle = await Create(subject);
        var path = $"{subject.WorkFolder}/data.bin";
        await subject.Provider.WriteFile(handle, path, new MemoryStream(FileContent), Cancellation);

        await subject.Provider.Pause(handle, Cancellation);
        var paused = await subject.Provider.Describe(handle, Cancellation);
        var resumed = await subject.Provider.Resume(handle, subject.Spec.IdleTimeout, Cancellation);

        paused.State.Should().Be(ProviderSandboxState.Paused);
        resumed.ProviderSandboxId.Should().Be(handle.ProviderSandboxId);
        (await ReadAll(subject, resumed, path)).Should().Equal(FileContent, "a file written before the pause survives it");
    }

    private static async Task Destroys(SandboxUnderTest subject)
    {
        var handle = await Create(subject);

        await subject.Provider.Destroy(handle, Cancellation);
        var info = await subject.Provider.Describe(handle, Cancellation);
        await subject.Provider.Destroy(handle, Cancellation);

        info.State.Should().Be(ProviderSandboxState.Gone);
    }

    #endregion

    #region Commands

    private static async Task RunsACommand(SandboxUnderTest subject)
    {
        var handle = await Create(subject);

        var outputs = await RunToEnd(subject, handle, CommandRequest.Shell(StreamingCommand));

        outputs[0].Should().BeOfType<CommandStarted>().Which.Pid.Should().BePositive();
        outputs.OfType<CommandStdout>().Should().HaveCountGreaterThan(1, "output streams as it is written");
        Text(outputs.OfType<CommandStdout>().Select(output => output.Data)).Should().Be("line1\nline2\nline3\n");
        Text(outputs.OfType<CommandStderr>().Select(output => output.Data)).Should().Be("oops\n");
        outputs[^1].Should().BeOfType<CommandExited>().Which.ExitCode.Should().Be(3);
        outputs.Take(outputs.Count - 1).Should().NotContain(output => output is CommandExited);
        ShouldBeDroverTypes([.. outputs]);
    }

    private static async Task ExitsZero(SandboxUnderTest subject)
    {
        var handle = await Create(subject);

        var outputs = await RunToEnd(subject, handle, CommandRequest.Shell("true"));

        outputs[^1].Should().BeOfType<CommandExited>().Which.ExitCode.Should().Be(0);
    }

    private static async Task KillsOnCancel(SandboxUnderTest subject)
    {
        var handle = await Create(subject);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);

        var running = async () =>
        {
            await foreach (var output in subject.Provider.Run(handle, LongSleep, cancel.Token))
            {
                if (output is CommandStarted)
                {
                    await cancel.CancelAsync();
                }
            }
        };

        await running.Should().ThrowAsync<OperationCanceledException>();
        await ShouldHaveNoSleepLeft(subject, handle);
    }

    private static async Task StopsAtTheTimeLimit(SandboxUnderTest subject)
    {
        var handle = await Create(subject);

        var running = () => RunToEnd(subject, handle, LongSleep with { Timeout = TimeSpan.FromSeconds(2) });

        (await running.Should().ThrowAsync<SandboxProviderException>()).Which.Failure.Should().Be(SandboxFailure.TimedOut);
        await ShouldHaveNoSleepLeft(subject, handle);
    }

    #endregion

    #region Files

    private static async Task RoundTripsBinary(SandboxUnderTest subject)
    {
        var handle = await Create(subject);
        var path = $"{subject.WorkFolder}/data.bin";

        await subject.Provider.WriteFile(handle, path, new MemoryStream(FileContent), Cancellation);

        (await ReadAll(subject, handle, path)).Should().Equal(FileContent);
    }

    private static async Task ListsFiles(SandboxUnderTest subject)
    {
        var handle = await Create(subject);
        await subject.Provider.WriteFile(handle, $"{subject.WorkFolder}/data.bin", new MemoryStream(FileContent), Cancellation);

        var entries = await subject.Provider.ListFiles(handle, subject.WorkFolder, Cancellation);

        var entry = entries.Should().ContainSingle().Which;
        entry.Name.Should().Be("data.bin");
        entry.Path.Should().EndWith("/data.bin");
        entry.Kind.Should().Be(SandboxEntryKind.File);
        entry.Size.Should().Be(FileContent.Length);
        entry.ModifiedAt.Should().NotBeNull();
        ShouldBeDroverTypes([.. entries]);
    }

    #endregion

    #region Network

    private static async Task DeniesNetwork(SandboxUnderTest subject)
    {
        var handle = await Create(subject);
        var reach = CommandRequest.Shell(ReachOutCommand) with { Timeout = TimeSpan.FromSeconds(30) };

        var open = await RunToEnd(subject, handle, reach);
        await subject.Provider.SetNetworkPolicy(handle, new NetworkPolicy(NetworkAccess.Off, []), Cancellation);
        var closed = await RunToEnd(subject, handle, reach);

        open[^1].Should().BeOfType<CommandExited>().Which.ExitCode.Should().Be(0, "the network starts open");
        closed[^1].Should().BeOfType<CommandExited>().Which.ExitCode.Should().NotBe(0).And.NotBe(127);
    }

    #endregion

    #region Refusals

    private static async Task RefusesAnotherGroupsHandle(SandboxUnderTest subject)
    {
        var handle = await Create(subject);

        foreach (var foreign in new[]
        {
            handle with { SharingGroupId = Guid.NewGuid() },
            handle with { SpaceId = Guid.NewGuid() },
        })
        {
            var sentBefore = subject.SentRequests().Count;

            foreach (var operation in Operations(subject, foreign))
            {
                var failure = (await operation.Should().ThrowAsync<SandboxProviderException>()).Which;
                failure.Failure.Should().Be(SandboxFailure.HandleMismatch);
                failure.Message.Should().NotContain(handle.AccessToken);
            }

            subject.SentRequests().Skip(sentBefore).Should().OnlyContain(
                request => request.Method == "GET",
                "a refused handle may only read the provider's record, never change or enter the sandbox");
        }

        (await subject.Provider.Describe(handle, Cancellation)).State.Should().Be(ProviderSandboxState.Running);
    }

    private static async Task RefusesAWrongToken(SandboxUnderTest subject)
    {
        var handle = await Create(subject);
        var wrong = handle with { AccessToken = $"wrong-{Guid.NewGuid():N}" };

        Func<Task>[] operations =
        [
            () => subject.Provider.ListFiles(wrong, subject.WorkFolder, Cancellation),
            () => subject.Provider.ReadFile(wrong, $"{subject.WorkFolder}/data.bin", Cancellation),
        ];

        foreach (var operation in operations)
        {
            var failure = (await operation.Should().ThrowAsync<SandboxProviderException>()).Which;
            failure.Failure.Should().Be(SandboxFailure.Unauthorized);
            failure.Message.Should().NotContain(wrong.AccessToken).And.NotContain(handle.AccessToken);
            failure.ToString().Should().NotContain(wrong.AccessToken).And.NotContain(handle.AccessToken);
        }
    }

    /// <summary>Every operation that takes a handle.</summary>
    private static List<Func<Task>> Operations(SandboxUnderTest subject, SandboxHandle handle) =>
    [
        () => subject.Provider.Describe(handle, Cancellation),
        () => subject.Provider.KeepAlive(handle, subject.Spec.IdleTimeout, Cancellation),
        () => subject.Provider.Pause(handle, Cancellation),
        () => subject.Provider.Resume(handle, subject.Spec.IdleTimeout, Cancellation),
        () => subject.Provider.Destroy(handle, Cancellation),
        () => RunToEnd(subject, handle, CommandRequest.Shell("true")),
        () => subject.Provider.ReadFile(handle, $"{subject.WorkFolder}/data.bin", Cancellation),
        () => subject.Provider.WriteFile(handle, $"{subject.WorkFolder}/data.bin", new MemoryStream(FileContent), Cancellation),
        () => subject.Provider.ListFiles(handle, subject.WorkFolder, Cancellation),
        () => subject.Provider.SetNetworkPolicy(handle, new NetworkPolicy(NetworkAccess.Open, []), Cancellation),
    ];

    #endregion

    #region Helpers

    private static Task<SandboxHandle> Create(SandboxUnderTest subject) =>
        subject.Provider.Create(subject.Spec, Cancellation);

    private static async Task<List<CommandOutput>> RunToEnd(SandboxUnderTest subject, SandboxHandle handle, CommandRequest command)
    {
        List<CommandOutput> outputs = [];

        await foreach (var output in subject.Provider.Run(handle, command, Cancellation))
        {
            outputs.Add(output);
        }

        return outputs;
    }

    private static async Task ShouldHaveNoSleepLeft(SandboxUnderTest subject, SandboxHandle handle)
    {
        var check = await RunToEnd(subject, handle, CommandRequest.Shell(NoSleepLeftCommand));

        check[^1].Should().BeOfType<CommandExited>().Which.ExitCode.Should().Be(1, "the abandoned command was killed");
    }

    private static async Task<byte[]> ReadAll(SandboxUnderTest subject, SandboxHandle handle, string path)
    {
        await using var content = await subject.Provider.ReadFile(handle, path, Cancellation);
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, Cancellation);

        return copy.ToArray();
    }

    private static string Text(IEnumerable<ReadOnlyMemory<byte>> pieces) =>
        string.Concat(pieces.Select(piece => Encoding.UTF8.GetString(piece.Span)));

    private static void ShouldBeDroverTypes(params object[] values) =>
        values.Should().OnlyContain(
            value => value.GetType().Assembly == typeof(ISandboxProvider).Assembly,
            "no vendor object may leave the adapter");

    #endregion
}
