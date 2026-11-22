namespace Drover.Server.Files;

/// <summary>Runs untrusted PDF parsing in a short-lived process with a hard deadline.</summary>
// This seam launches only this server assembly, with fixed arguments and no inherited environment.
#pragma warning disable RS0030
internal static class PdfPreviewWorker
{
    #region Private Fields

    internal const string Argument = "--pdf-preview-worker";

    private const int MaxErrorBytes = 64 * 1024;

    private const int MaxOutputBytes = 16 * 1024 * 1024;

    private const string WorkerHeapLimit = "8000000";

    #endregion

    #region Public Methods

    internal static async Task<NativeFilePreview> Read(
        byte[] content,
        TimeSpan deadline,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Process? process = null;
        var started = false;
        try
        {
            process = new() { StartInfo = BuildStartInfo() };
            started = process.Start();
            if (!started)
            {
                return new(Refusal: NativePreviewRefusal.TooComplex);
            }

            using var deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadlineCancellation.CancelAfter(deadline);
            var workerToken = deadlineCancellation.Token;
            var output = ReadBounded(process.StandardOutput.BaseStream, MaxOutputBytes, workerToken);
            var error = ReadBounded(process.StandardError.BaseStream, MaxErrorBytes, workerToken);
            var input = WriteInput(process.StandardInput.BaseStream, content, workerToken);

            try
            {
                await Task.WhenAll(process.WaitForExitAsync(workerToken), input, output, error);
            }
            catch (OperationCanceledException failed)
            {
                await Stop(process);
                cancellationToken.ThrowIfCancellationRequested();
                return RefusedFor(failed);
            }
            catch (Exception failed) when (failed is IOException or InvalidOperationException)
            {
                await Stop(process);
                return RefusedFor(failed);
            }

            if (process.ExitCode != 0)
            {
                return new(Refusal: NativePreviewRefusal.TooComplex);
            }

            var response = await output;
            return JsonSerializer.Deserialize<NativeFilePreview>(response, JsonSerializerOptions.Web)
                ?? new(Refusal: NativePreviewRefusal.TooComplex);
        }
        catch (Exception failed) when (failed is Win32Exception or JsonException
            or InvalidOperationException or NotSupportedException)
        {
            return RefusedFor(failed);
        }
        finally
        {
            if (process is not null)
            {
                if (started && !process.HasExited)
                {
                    await Stop(process);
                }

                process.Dispose();
            }
        }
    }

    internal static async Task<int> Run(
        Stream input,
        Stream output,
        CancellationToken cancellationToken)
    {
        var content = await ReadBounded(input, NativeFilePreview.MaxPdfBytes + 1, cancellationToken);
        var result = content.Length > NativeFilePreview.MaxPdfBytes
            ? new(Refusal: NativePreviewRefusal.TooLarge)
            : DocumentReader.ReadPdfPreview(content, cancellationToken);
        await JsonSerializer.SerializeAsync(
            output,
            result,
            JsonSerializerOptions.Web,
            cancellationToken);
        return 0;
    }

    #endregion

    #region Private Methods

    private static ProcessStartInfo BuildStartInfo()
    {
        var serverAssembly = typeof(PdfPreviewWorker).Assembly.Location;
        var processPath = Environment.ProcessPath;
        var runsAsAppHost = processPath is not null
            && string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                Path.GetFileNameWithoutExtension(serverAssembly),
                StringComparison.OrdinalIgnoreCase);
        var start = new ProcessStartInfo
        {
            FileName = runsAsAppHost ? processPath! : FindDotnetHost(processPath),
            WorkingDirectory = Path.GetDirectoryName(serverAssembly)!,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.Environment.Clear();
        start.Environment["DOTNET_EnableDiagnostics"] = "0";
        start.Environment["DOTNET_GCHeapHardLimit"] = WorkerHeapLimit;
        if (!runsAsAppHost)
        {
            start.ArgumentList.Add(serverAssembly);
        }

        start.ArgumentList.Add(Argument);
        return start;
    }

    private static string FindDotnetHost(string? processPath)
    {
        if (processPath is not null
            && string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } hostPath)
        {
            return hostPath;
        }

        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } dotnetRoot)
        {
            var rootedHost = Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(rootedHost))
            {
                return rootedHost;
            }
        }

        return OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
    }

    private static async Task<byte[]> ReadBounded(
        Stream stream,
        int limit,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (buffer.Length <= limit)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > limit)
            {
                throw new IOException($"PDF preview worker output exceeded {limit} bytes.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        throw new IOException($"PDF preview worker output exceeded {limit} bytes.");
    }

    private static NativeFilePreview RefusedFor(Exception failed) =>
        new(Refusal: failed is JsonException
            ? NativePreviewRefusal.Malformed
            : NativePreviewRefusal.TooComplex);

    private static async Task Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    private static async Task WriteInput(Stream input, byte[] content, CancellationToken cancellationToken)
    {
        await input.WriteAsync(content, cancellationToken);
        await input.FlushAsync(cancellationToken);
        input.Close();
    }

    #endregion
}
#pragma warning restore RS0030
