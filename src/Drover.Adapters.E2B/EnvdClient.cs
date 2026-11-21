namespace Drover.Adapters.E2B;

/// <summary>
/// The daemon inside one sandbox, at <c>https://49983-{id}.{domain}</c>. Files go over REST; processes and folder
/// listings go over Connect RPC with the JSON codec. Every call carries the sandbox's access token, and RPCs name the
/// sandbox's user with HTTP Basic and an empty password, as E2B's own SDK does.
/// </summary>
internal sealed class EnvdClient(IHttpClientFactory httpClients, E2BOptions options, TimeProvider time, ILogger logger)
{
    #region Private Fields

    public const int Port = 49983;

    public const string User = "user";

    private const string AccessTokenHeader = "X-Access-Token";

    private const string ConnectJson = "application/connect+json";

    /// <summary>
    /// How long the daemon has to start answering a small call, and for an upload, from the last byte sent. A
    /// command's output then runs to its own time limit.
    /// </summary>
    private static readonly TimeSpan headersTimeout = TimeSpan.FromMinutes(1);

    /// <summary>An upload fails when no byte reaches the connection for this long.</summary>
    private static readonly TimeSpan uploadIdleTimeout = TimeSpan.FromSeconds(30);

    /// <summary>An upload cannot hold a connection open past this, however well the bytes keep moving.</summary>
    private static readonly TimeSpan uploadCeiling = TimeSpan.FromMinutes(30);

    /// <summary>Extra time past the command's limit for the daemon's own deadline answer to arrive first.</summary>
    private static readonly TimeSpan deadlineGrace = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan killTimeout = TimeSpan.FromSeconds(10);

    private static readonly string basicUser = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{User}:"));

    #endregion

    #region Public Methods

    public static Uri Address(string sandboxId, string domain) => new($"https://{Port}-{sandboxId}.{domain}/");

    public async IAsyncEnumerable<CommandOutput> Start(
        SandboxHandle sandbox,
        CommandRequest command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(command.Timeout + deadlineGrace);

        using var request = BuildStart(sandbox, command);
        using var response = await Send(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken, deadline.Token);
        await using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
        await using var frames = ConnectEnvelope.Read(body, deadline.Token).GetAsyncEnumerator(deadline.Token);

        int? pid = null;
        var exited = false;

        try
        {
            while (await NextOutput(frames, cancellationToken) is { } output)
            {
                if (output is CommandStarted started)
                {
                    pid = started.Pid;
                    if (command.StandardInput is { } input)
                    {
                        await SendInput(sandbox, started.Pid, input, deadline.Token);
                    }
                }

                exited = output is CommandExited;

                yield return output;

                if (exited)
                {
                    yield break;
                }
            }

            throw FailureMapping.EndedEarly();
        }
        finally
        {
            if (!exited && pid is { } running)
            {
                await Kill(sandbox, running);
            }
        }
    }

    /// <summary>The file's bytes as they arrive. Disposing the stream releases the connection.</summary>
    public async Task<Stream> ReadFile(SandboxHandle sandbox, string path, CancellationToken cancellationToken)
    {
        using var request = Build(sandbox, HttpMethod.Get, FilePath(path));
        var response = await Send(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken, cancellationToken);

        try
        {
            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }
        catch (Exception exception) when (FailureMapping.TryMapTransport(exception, out var mapped))
        {
            response.Dispose();
            logger.LogWarning("Reading a file from sandbox {SandboxId} failed with {Failure}", sandbox.ProviderSandboxId, mapped.Failure);
            throw mapped;
        }
    }

    public async Task WriteFile(SandboxHandle sandbox, string path, Stream content, CancellationToken cancellationToken)
    {
        using var request = Build(sandbox, HttpMethod.Post, FilePath(path));
        using var upload = new IdleTimedContent(content, time, uploadIdleTimeout, headersTimeout, uploadCeiling, cancellationToken);
        request.Content = upload;
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await Send(request, HttpCompletionOption.ResponseContentRead, null, cancellationToken, upload.Deadline);
    }

    public async Task<IReadOnlyList<SandboxEntry>> ListDir(SandboxHandle sandbox, string path, CancellationToken cancellationToken)
    {
        using var request = BuildUnary(sandbox, "filesystem.Filesystem/ListDir", new ListDirRequest(path, 1));
        using var response = await Send(request, HttpCompletionOption.ResponseContentRead, cancellationToken, cancellationToken);

        ListDirReply? reply;

        try
        {
            reply = await response.Content.ReadFromJsonAsync<ListDirReply>(Wire.Json, cancellationToken);
        }
        catch (JsonException)
        {
            throw FailureMapping.Malformed();
        }

        return [.. (reply?.Entries ?? []).Select(ToEntry)];
    }

    #endregion

    #region Process Control

    private async Task SendInput(SandboxHandle sandbox, int pid, ReadOnlyMemory<byte> input, CancellationToken cancellation)
    {
        var process = new ProcessSelector(pid);
        using var write = BuildUnary(sandbox, "process.Process/SendInput", new SendInputRequest(process, new(input)));
        using var written = await Send(write, HttpCompletionOption.ResponseContentRead, cancellation, cancellation);
        using var close = BuildUnary(sandbox, "process.Process/CloseStdin", new CloseStdinRequest(process));
        using var closed = await Send(close, HttpCompletionOption.ResponseContentRead, cancellation, cancellation);
    }

    private HttpRequestMessage BuildStart(SandboxHandle sandbox, CommandRequest command)
    {
        var start = new StartRequest(
            new ProcessConfig(command.Program, command.Arguments, command.Environment, command.WorkingDirectory),
            Stdin: command.StandardInput is not null);

        var request = Build(sandbox, HttpMethod.Post, "process.Process/Start");
        request.Headers.TryAddWithoutValidation("Authorization", $"Basic {basicUser}");
        request.Headers.TryAddWithoutValidation("Connect-Protocol-Version", "1");
        request.Headers.TryAddWithoutValidation(
            "Connect-Timeout-Ms",
            ((long)command.Timeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("Keepalive-Ping-Interval", "50");
        request.Content = new ByteArrayContent(ConnectEnvelope.Frame(JsonSerializer.SerializeToUtf8Bytes(start, Wire.Json)));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(ConnectJson);

        return request;
    }

    /// <summary>
    /// The next output, skipping keep-alives. Null at the end of the stream. The caller's cancellation stays
    /// cancellation; the command's own deadline becomes <see cref="SandboxFailure.TimedOut"/>.
    /// </summary>
    private static async Task<CommandOutput?> NextOutput(IAsyncEnumerator<JsonElement> frames, CancellationToken cancellationToken)
    {
        try
        {
            while (await frames.MoveNextAsync())
            {
                if (ProcessEvents.Read(frames.Current) is { } output)
                {
                    return output;
                }
            }

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw FailureMapping.TimedOut();
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            && FailureMapping.TryMapTransport(exception, out var mapped))
        {
            throw mapped;
        }
    }

    /// <summary>
    /// Disconnecting does not stop a process in an E2B sandbox, so a command abandoned before it exits is killed.
    /// This runs after the caller has given up, so it does not use the caller's cancellation.
    /// </summary>
    private async Task Kill(SandboxHandle sandbox, int pid)
    {
        using var timeout = new CancellationTokenSource(killTimeout);

        try
        {
            using var request = BuildUnary(
                sandbox,
                "process.Process/SendSignal",
                new SendSignalRequest(new ProcessSelector(pid), "SIGNAL_SIGKILL"));
            using var response = await Send(request, HttpCompletionOption.ResponseContentRead, timeout.Token, timeout.Token);
        }
        catch (Exception exception) when (exception is SandboxProviderException or OperationCanceledException)
        {
            logger.LogWarning(
                "Could not stop process {Pid} in sandbox {SandboxId}: {Failure}",
                pid,
                sandbox.ProviderSandboxId,
                exception is SandboxProviderException failure ? failure.Failure.ToString() : "timed out");
        }
    }

    #endregion

    #region Private Methods

    private static string FilePath(string path) =>
        $"files?path={Uri.EscapeDataString(path)}&username={User}";

    private static SandboxEntry ToEntry(EntryInfo entry) =>
        new(
            entry.Name,
            entry.Path,
            entry.Type switch
            {
                "FILE_TYPE_FILE" => SandboxEntryKind.File,
                "FILE_TYPE_DIRECTORY" => SandboxEntryKind.Directory,
                "FILE_TYPE_SYMLINK" => SandboxEntryKind.Symlink,
                _ => SandboxEntryKind.Other,
            },
            entry.Size,
            ProtoTime.Parse(entry.ModifiedTime));

    private HttpRequestMessage BuildUnary(SandboxHandle sandbox, string method, object body)
    {
        var request = Build(sandbox, HttpMethod.Post, method);
        request.Headers.TryAddWithoutValidation("Authorization", $"Basic {basicUser}");
        request.Headers.TryAddWithoutValidation("Connect-Protocol-Version", "1");
        request.Content = JsonContent.Create(body, body.GetType(), options: Wire.Json);

        return request;
    }

    private HttpRequestMessage Build(SandboxHandle sandbox, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(Address(sandbox.ProviderSandboxId, options.Domain), path));
        request.Headers.TryAddWithoutValidation(AccessTokenHeader, sandbox.AccessToken);

        return request;
    }

    /// <summary>
    /// Sends with a limit on waiting for the answer to start. <paramref name="caller"/> is the caller's own
    /// cancellation, which is never turned into a failure; <paramref name="cancellationToken"/> may be a deadline.
    /// </summary>
    private Task<HttpResponseMessage> Send(
        HttpRequestMessage request,
        HttpCompletionOption completion,
        CancellationToken caller,
        CancellationToken cancellationToken) =>
        Send(request, completion, headersTimeout, caller, cancellationToken);

    /// <summary>
    /// An upload brings its own deadline, which arms the answer limit after the last byte. A null
    /// <paramref name="answerTimeout"/> leaves that to the body instead of timing the whole call from its start.
    /// </summary>
    private async Task<HttpResponseMessage> Send(
        HttpRequestMessage request,
        HttpCompletionOption completion,
        TimeSpan? answerTimeout,
        CancellationToken caller,
        CancellationToken cancellationToken)
    {
        using var answer = new CancellationTokenSource(Timeout.InfiniteTimeSpan, time);

        if (answerTimeout is { } limit)
        {
            answer.CancelAfter(limit);
        }

        using var headers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, answer.Token);

        HttpResponseMessage response;

        try
        {
            response = await httpClients.CreateClient(E2BSandboxProvider.EnvdClientName).SendAsync(request, completion, headers.Token);
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            logger.LogWarning("The sandbox daemon at {Host} did not answer in time", request.RequestUri?.Host);
            throw FailureMapping.Unreachable();
        }
        catch (Exception exception) when (FailureMapping.TryMapTransport(exception, out var mapped))
        {
            logger.LogWarning("The sandbox daemon at {Host} failed with {Failure}", request.RequestUri?.Host, mapped.Failure);
            throw mapped;
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            throw await ProviderErrors.Read(response, Plane.Sandbox, logger, caller);
        }
    }

    #endregion
}
