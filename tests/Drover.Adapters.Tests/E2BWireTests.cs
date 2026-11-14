namespace Drover.Adapters.Tests;

/// <summary>The E2B adapter's wire details: Connect framing, failure mapping, network rules, and what it sends.</summary>
public sealed class E2BWireTests
{
    #region Connect Framing

    [Fact]
    public void A_frame_is_a_flag_byte_a_big_endian_length_and_the_message()
    {
        var frame = ConnectEnvelope.Frame("{}"u8);

        frame.Should().Equal(0x00, 0x00, 0x00, 0x00, 0x02, (byte)'{', (byte)'}');
    }

    [Fact]
    public async Task Frames_are_read_whole_when_they_arrive_one_byte_at_a_time()
    {
        var stream = Frames((0, """{"event":{"start":{"pid":7}}}"""), (0, """{"event":{"keepalive":{}}}"""), (2, "{}"));

        var messages = await ReadAll(new TrickleStream(stream));

        messages.Select(message => message.GetRawText()).Should().Equal(
            """{"event":{"start":{"pid":7}}}""",
            """{"event":{"keepalive":{}}}""");
    }

    [Theory]
    [InlineData("deadline_exceeded", SandboxFailure.TimedOut)]
    [InlineData("resource_exhausted", SandboxFailure.RateLimited)]
    [InlineData("unauthenticated", SandboxFailure.Unauthorized)]
    [InlineData("not_found", SandboxFailure.NotFound)]
    [InlineData("internal", SandboxFailure.Unavailable)]
    public async Task An_error_at_the_end_of_the_stream_becomes_its_failure(string code, SandboxFailure expected)
    {
        var stream = Frames((0, """{"event":{"start":{"pid":7}}}"""), (2, $$$"""{"error":{"code":"{{{code}}}","message":"secret detail"}}"""));

        var failure = await FluentActions.Awaiting(() => ReadAll(stream)).Should().ThrowAsync<SandboxProviderException>();

        failure.Which.Failure.Should().Be(expected);
        failure.Which.Message.Should().NotContain("secret detail", "the provider's own text is never passed on");
    }

    [Fact]
    public async Task A_compressed_frame_is_refused()
    {
        var failure = await FluentActions.Awaiting(() => ReadAll(Frames((1, "{}")))).Should().ThrowAsync<SandboxProviderException>();

        failure.Which.Failure.Should().Be(SandboxFailure.Unavailable);
    }

    [Fact]
    public async Task A_frame_longer_than_the_limit_is_refused_before_it_is_read()
    {
        var header = new byte[5];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(1), uint.MaxValue);

        var failure = await FluentActions.Awaiting(() => ReadAll(new MemoryStream(header))).Should().ThrowAsync<SandboxProviderException>();

        failure.Which.Failure.Should().Be(SandboxFailure.Unavailable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_stream_that_stops_without_its_end_frame_or_mid_frame_is_incomplete(bool midFrame)
    {
        var whole = Frames((0, """{"event":{"start":{"pid":7}}}""")).ToArray();
        var cut = midFrame ? whole[..^3] : whole;

        var failure = await FluentActions.Awaiting(() => ReadAll(new MemoryStream(cut))).Should().ThrowAsync<SandboxProviderException>();

        failure.Which.Failure.Should().Be(SandboxFailure.Unavailable);
    }

    [Theory]
    [InlineData("""{"event":{"end":{"exited":true,"status":"exit status 0"}}}""", 0)]
    [InlineData("""{"event":{"end":{"exitCode":-1,"exited":true}}}""", -1)]
    public void A_missing_exit_code_is_zero(string message, int expected) =>
        ProcessEvents.Read(JsonElement.Parse(message)).Should().Be(new CommandExited(expected, null));

    [Fact]
    public void Output_bytes_are_base64_and_pty_output_counts_as_stdout()
    {
        var stdout = ProcessEvents.Read(JsonElement.Parse("""{"event":{"data":{"stdout":"AP8="}}}"""));
        var pty = ProcessEvents.Read(JsonElement.Parse("""{"event":{"data":{"pty":"aGk="}}}"""));

        stdout.Should().BeOfType<CommandStdout>().Which.Data.ToArray().Should().Equal(0x00, 0xff);
        pty.Should().BeOfType<CommandStdout>().Which.Data.ToArray().Should().Equal((byte)'h', (byte)'i');
        ProcessEvents.Read(JsonElement.Parse("""{"event":{"keepalive":{}}}""")).Should().BeNull();
    }

    [Fact]
    public void Proto_times_with_nine_fraction_digits_are_read()
    {
        ProtoTime.Parse("2026-11-14T15:43:18.198870680Z").Should().Be(
            new DateTimeOffset(2026, 11, 14, 15, 43, 18, TimeSpan.Zero).AddTicks(1988706));
    }

    #endregion

    #region Failure Mapping

    [Theory]
    [InlineData(401, false, SandboxFailure.InvalidKey)]
    [InlineData(401, true, SandboxFailure.Unauthorized)]
    [InlineData(403, true, SandboxFailure.Unauthorized)]
    [InlineData(402, false, SandboxFailure.QuotaExceeded)]
    [InlineData(404, false, SandboxFailure.NotFound)]
    [InlineData(409, false, SandboxFailure.Rejected)]
    [InlineData(429, false, SandboxFailure.RateLimited)]
    [InlineData(503, false, SandboxFailure.RetryLater)]
    [InlineData(502, true, SandboxFailure.Unavailable)]
    [InlineData(507, true, SandboxFailure.TooLarge)]
    public void Statuses_map_to_failures(int status, bool fromTheSandbox, SandboxFailure expected) =>
        FailureMapping.FromStatus(status, fromTheSandbox ? Plane.Sandbox : Plane.ControlPlane).Failure.Should().Be(expected);

    [Fact]
    public void Should_retry_and_an_idempotent_creation_in_progress_mean_retry_later()
    {
        FailureMapping.FromError(new ProviderError(400, null, true, null), Plane.ControlPlane).Failure
            .Should().Be(SandboxFailure.RetryLater);
        FailureMapping.FromError(new ProviderError(409, "idempotency_in_progress", null, null), Plane.ControlPlane).Failure
            .Should().Be(SandboxFailure.RetryLater);
        FailureMapping.FromError(new ProviderError(400, null, false, null), Plane.ControlPlane).Failure
            .Should().Be(SandboxFailure.Rejected);
    }

    [Fact]
    public void A_rate_limit_keeps_the_wait_the_provider_asked_for()
    {
        var failure = FailureMapping.FromError(new ProviderError(429, null, null, TimeSpan.FromSeconds(7)), Plane.ControlPlane);

        failure.RetryAfter.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task A_malformed_key_is_reported_as_an_invalid_key_without_echoing_it()
    {
        var rig = E2BRig.Start("malformed_key");

        var failure = await FluentActions.Awaiting(() => rig.Subject.Provider.Create(rig.Subject.Spec, CancellationToken.None))
            .Should().ThrowAsync<SandboxProviderException>();

        failure.Which.Failure.Should().Be(SandboxFailure.InvalidKey);
        rig.Handler.Requests.Single().Headers.Should().ContainKey("X-API-Key");
        failure.Which.ToString().Should().NotContain(rig.Handler.Requests.Single().Headers["X-API-Key"]);
    }

    #endregion

    #region Network Rules

    [Fact]
    public void Off_denies_all_traffic_and_create_turns_off_public_inbound_traffic()
    {
        var created = NetworkRules.ForCreate(new NetworkPolicy(NetworkAccess.Off, ["example.com"]));
        var updated = NetworkRules.ForUpdate(new NetworkPolicy(NetworkAccess.Off, []));

        created.Should().Be(created with { AllowOut = null, AllowPublicTraffic = false });
        created.DenyOut.Should().Equal("0.0.0.0/0");
        updated.AllowPublicTraffic.Should().BeNull("the update shape has no inbound setting");
        updated.DenyOut.Should().Equal("0.0.0.0/0");
    }

    [Fact]
    public void An_allowlist_allows_its_domains_and_denies_everything_else()
    {
        var rules = NetworkRules.ForUpdate(new NetworkPolicy(NetworkAccess.Allowlist, ["Example.com ", "*.pypi.org"]));

        rules.AllowOut.Should().Equal("example.com", "*.pypi.org");
        rules.DenyOut.Should().Equal("0.0.0.0/0");
    }

    [Theory]
    [InlineData("10.0.0.0/8")]
    [InlineData("0.0.0.0/0")]
    [InlineData("169.254.169.254")]
    [InlineData("https://example.com")]
    [InlineData("*")]
    [InlineData("")]
    public void An_allowlist_entry_that_is_not_a_domain_name_is_refused(string host)
    {
        var refusal = FluentActions.Invoking(() => NetworkRules.ForUpdate(new NetworkPolicy(NetworkAccess.Allowlist, [host])));

        refusal.Should().Throw<SandboxProviderException>().Which.Failure.Should().Be(SandboxFailure.Rejected);
    }

    [Fact]
    public void Open_still_denies_private_and_metadata_ranges()
    {
        var rules = NetworkRules.ForUpdate(new NetworkPolicy(NetworkAccess.Open, []));

        rules.AllowOut.Should().BeNull();
        rules.DenyOut.Should().BeEquivalentTo(
            ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "100.64.0.0/10", "169.254.0.0/16"]);
    }

    #endregion

    #region What Is Sent

    [Fact]
    public void The_daemon_is_reached_on_the_direct_host_for_the_sandbox()
    {
        EnvdClient.Address("abc123", "e2b.dev").Should().Be(new Uri("https://49983-abc123.e2b.dev/"));
    }

    [Fact]
    public async Task Create_sends_the_template_ttl_auto_pause_metadata_network_and_idempotency_key()
    {
        var rig = E2BRig.Start(SandboxScenarios.CreatesWithMetadata);

        await rig.Subject.Provider.Create(rig.Subject.Spec, CancellationToken.None);

        var create = rig.Handler.Requests.Single();
        create.Uri.Should().Be(new Uri("https://api.e2b.dev/v2/sandboxes"));
        create.Headers["Idempotency-Key"].Should().Be(E2BRig.IdempotencyKey.ToString("D"));
        create.Json.GetProperty("templateID").GetString().Should().Be("base");
        create.Json.GetProperty("timeout").GetInt32().Should().Be(120);
        create.Json.GetProperty("autoPause").GetBoolean().Should().BeTrue();
        create.Json.GetProperty("autoPauseMemory").GetBoolean().Should().BeTrue();
        create.Json.GetProperty("metadata").GetProperty("drover.space").GetString().Should().Be(E2BRig.SpaceId.ToString("D"));
        create.Json.GetProperty("metadata").GetProperty("drover.group").GetString().Should().Be(E2BRig.SharingGroupId.ToString("D"));
        create.Json.GetProperty("network").GetProperty("allowPublicTraffic").GetBoolean().Should().BeFalse();
        create.Json.GetProperty("network").GetProperty("denyOut").GetArrayLength().Should().Be(5);
        create.Json.TryGetProperty("envVars", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Every_operation_reads_the_providers_record_before_it_acts()
    {
        var rig = E2BRig.Start(SandboxScenarios.RoundTripsBinary);
        var handle = await rig.Subject.Provider.Create(rig.Subject.Spec, CancellationToken.None);

        await rig.Subject.Provider.WriteFile(handle, "/home/user/probe/data.bin", new MemoryStream([1, 2]), CancellationToken.None);

        rig.Handler.Requests.Skip(1).Select(request => $"{request.Method} {request.Uri.Host}{request.Uri.AbsolutePath}").Should().Equal(
            $"GET api.e2b.dev/sandboxes/{E2BRig.SandboxId}",
            $"POST 49983-{E2BRig.SandboxId}.e2b.dev/files");
    }

    [Fact]
    public async Task A_command_is_one_connect_frame_with_the_token_the_user_and_the_deadline()
    {
        var rig = E2BRig.Start(SandboxScenarios.ExitsZero);
        var handle = await rig.Subject.Provider.Create(rig.Subject.Spec, CancellationToken.None);
        var command = new CommandRequest { Program = "/bin/ls", Arguments = ["-la", "; rm -rf /"], Timeout = TimeSpan.FromSeconds(9) };

        await foreach (var output in rig.Subject.Provider.Run(handle, command, CancellationToken.None))
        {
            output.Should().NotBeNull();
        }

        var start = rig.Handler.Requests.Single(request => request.Uri.AbsolutePath == "/process.Process/Start");
        start.Headers["X-Access-Token"].Should().Be("fixture-envd-token");
        start.Headers["Authorization"].Should().Be($"Basic {Convert.ToBase64String("user:"u8)}");
        start.Headers["Connect-Timeout-Ms"].Should().Be("9000");
        start.ContentHeaders["Content-Type"].Should().Be("application/connect+json");
        start.Bytes[0].Should().Be(0);
        BinaryPrimitives.ReadInt32BigEndian(start.Bytes.AsSpan(1)).Should().Be(start.Bytes.Length - 5);

        var body = JsonElement.Parse(start.Bytes.AsSpan(5));
        body.GetProperty("process").GetProperty("cmd").GetString().Should().Be("/bin/ls");
        body.GetProperty("process").GetProperty("args").EnumerateArray().Select(arg => arg.GetString())
            .Should().Equal("-la", "; rm -rf /");
        body.GetProperty("stdin").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_cancelled_command_is_killed_by_pid()
    {
        var rig = E2BRig.Start(SandboxScenarios.KillsOnCancel);
        var handle = await rig.Subject.Provider.Create(rig.Subject.Spec, CancellationToken.None);
        using var cancel = new CancellationTokenSource();

        var running = async () =>
        {
            await foreach (var output in rig.Subject.Provider.Run(handle, SandboxProviderConformance.LongSleep, cancel.Token))
            {
                await cancel.CancelAsync();
            }
        };

        await running.Should().ThrowAsync<OperationCanceledException>();
        var signal = rig.Handler.Requests.Single(request => request.Uri.AbsolutePath == "/process.Process/SendSignal");
        signal.Json.GetProperty("process").GetProperty("pid").GetInt32().Should().Be(1543);
        signal.Json.GetProperty("signal").GetString().Should().Be("SIGNAL_SIGKILL");
        signal.Headers["X-Access-Token"].Should().Be("fixture-envd-token");
    }

    [Theory]
    [InlineData("evil.example.com/x")]
    [InlineData("UPPER")]
    [InlineData("")]
    public async Task A_handle_whose_id_is_not_an_e2b_id_is_refused_before_any_request(string sandboxId)
    {
        var rig = E2BRig.Start(SandboxScenarios.CreatesWithMetadata);

        var failure = await FluentActions
            .Awaiting(() => rig.Subject.Provider.ListFiles(E2BRig.Handle() with { ProviderSandboxId = sandboxId }, "/", CancellationToken.None))
            .Should().ThrowAsync<SandboxProviderException>();

        failure.Which.Failure.Should().Be(SandboxFailure.HandleMismatch);
        rig.Handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_paused_sandbox_is_not_entered()
    {
        var fixture = RecordedFixture.Load("e2b", SandboxScenarios.PausesAndResumes);
        var onlyPaused = E2BRig.Start(new RecordedFixture
        {
            Provenance = fixture.Provenance,
            Note = fixture.Note,
            Exchanges = [.. fixture.Exchanges.Where(exchange => exchange.Body.Contains("\"state\":\"paused\""))],
        });

        var failure = await FluentActions
            .Awaiting(() => onlyPaused.Subject.Provider.ListFiles(E2BRig.Handle(), "/home/user", CancellationToken.None))
            .Should().ThrowAsync<SandboxProviderException>();

        failure.Which.Failure.Should().Be(SandboxFailure.Rejected);
        onlyPaused.Handler.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task No_key_means_no_request()
    {
        var handler = new RecordedHandler(RecordedFixture.Load("e2b", SandboxScenarios.CreatesWithMetadata));
        var provider = new E2BSandboxProvider(
            new FixtureHttpClientFactory(handler),
            Options.Create(new E2BOptions()),
            NullLogger<E2BSandboxProvider>.Instance);

        var failure = await FluentActions.Awaiting(() => provider.Create(E2BRig.Spec(), CancellationToken.None))
            .Should().ThrowAsync<SandboxProviderException>();

        failure.Which.Failure.Should().Be(SandboxFailure.InvalidKey);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public void Public_registration_resolves_the_provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddE2BSandboxProvider(options => options.ApiKey = TestKeys.Generate());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        provider.GetRequiredService<ISandboxProvider>().Kind.Should().Be("e2b");
    }

    #endregion

    #region Helpers

    private static MemoryStream Frames(params (byte Flags, string Json)[] frames)
    {
        var stream = new MemoryStream();

        foreach (var (flags, json) in frames)
        {
            var frame = ConnectEnvelope.Frame(Encoding.UTF8.GetBytes(json));
            frame[0] = flags;
            stream.Write(frame);
        }

        stream.Position = 0;

        return stream;
    }

    private static async Task<List<JsonElement>> ReadAll(Stream stream)
    {
        List<JsonElement> messages = [];

        await foreach (var message in ConnectEnvelope.Read(stream, CancellationToken.None))
        {
            messages.Add(message);
        }

        return messages;
    }

    /// <summary>Hands out one byte per read, so every frame crosses read boundaries.</summary>
    private sealed class TrickleStream(Stream inner) : Stream
    {
        #region Properties

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        #endregion

        #region Public Methods

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, 1));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        #endregion
    }

    #endregion
}
