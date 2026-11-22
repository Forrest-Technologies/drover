namespace Drover.Native.UITests;

/// <summary>
/// The manual proof of the Android basics, on the one emulator adb sees: the app installed on it is the build made for
/// the checks, and it talks to the test server of this run. The app's own steps send a message, attach a picture and
/// open the sandbox screen; this class does what the app cannot do from inside: it chooses the picture in the
/// system's file picker, drags a finger over the sandbox picture with real touch events, and then asks the server what
/// reached it. Each of the four things prints one pass or fail line. Everything a failure needs is kept in the
/// artifacts folder: a screenshot, the device log, the last screen the harness read, and a recording of the run.
/// </summary>
internal sealed class AndroidProof(ILogger logger, string artifacts)
{
    #region Private Fields

    private const string DeviceRecording = "/sdcard/drover-proof.mp4";

    private const string DeviceScreen = "/sdcard/drover-proof-ui.xml";

    private static readonly TimeSpan patience = TimeSpan.FromMinutes(6);

    private static readonly TimeSpan pickPatience = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan dragPatience = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan poll = TimeSpan.FromSeconds(1);

    private readonly List<(string Step, bool Passed, string Detail)> verdicts = [];

    private string lastScreen = string.Empty;

    #endregion

    #region Properties

    /// <summary>The name of the picture the proof attaches. The app is told it and looks for it in the conversation.</summary>
    public const string PictureName = "drover-proof.png";

    #endregion

    #region Public Methods

    /// <summary>Runs the proof and says whether every one of its four things held.</summary>
    public async Task<bool> Run(
        CheckAccount account,
        MemorySandboxProvider sandbox,
        byte[] attachment,
        IReadOnlyDictionary<string, string> settings)
    {
        Directory.CreateDirectory(artifacts);
        var serial = await AndroidApp.ChooseDevice();
        var port = new Uri(settings["DROVER_UI_CHECK_SERVER"]).Port;
        var runId = settings["DROVER_UI_CHECK_RUN"];
        var local = Path.Combine(artifacts, PictureName);
        await File.WriteAllBytesAsync(local, attachment);
        await AndroidApp.Adb(serial, "reverse", $"tcp:{port}", $"tcp:{port}");
        await AndroidApp.Adb(serial, "push", local, $"/sdcard/Download/{PictureName}");
        await AndroidApp.Adb(serial, "shell", "am", "broadcast", "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE", "-d", $"file:///sdcard/Download/{PictureName}");
        using var recording = StartRecording(serial);

        try
        {
            await AndroidApp.Launch(serial, settings);
            var lines = await Drive(serial, runId, sandbox);
            await Judge(account, sandbox, attachment, lines);
        }
        finally
        {
            await Collect(serial, runId, recording);
            await AndroidApp.Adb(serial, "shell", "am", "force-stop", AndroidApp.Package);
            await AndroidApp.Adb(serial, "reverse", "--remove", $"tcp:{port}");
        }

        await File.WriteAllLinesAsync(
            Path.Combine(artifacts, "proof-summary.md"),
            verdicts.Select(verdict => $"- {(verdict.Passed ? "PASS" : "FAIL")} {verdict.Step}: {verdict.Detail}"));
        return verdicts.Count is 4 && verdicts.All(verdict => verdict.Passed);
    }

    #endregion

    #region Driving

    // Reads the app's report until it says it is done or has failed. When it says it is waiting for something only the
    // harness can do, the harness does it: choosing the picture in the picker, and the drag.
    private async Task<IReadOnlyList<string>> Drive(string serial, string runId, MemorySandboxProvider sandbox)
    {
        var watch = Stopwatch.StartNew();
        var picked = false;
        var dragged = false;
        IReadOnlyList<string> lines = [];

        while (watch.Elapsed < patience)
        {
            lines = AndroidCheckGuard.ReportsForRun(
                await AndroidApp.Adb(serial, "logcat", "-d", "-v", "epoch", "-s", $"{AndroidApp.Tag}:I"), runId);

            if (lines.Any(line => line.StartsWith("ui-check fail", StringComparison.Ordinal)))
            {
                return lines;
            }

            if (!picked && lines.Any(line => line.StartsWith("ui-check wait: choose", StringComparison.Ordinal)))
            {
                picked = true;
                await Pick(serial);
            }
            else if (!dragged && lines.Any(line => line.StartsWith("ui-check wait: drag", StringComparison.Ordinal)))
            {
                dragged = true;
                await Drag(serial, sandbox);
            }
            else if (dragged && lines.Any(line => line.StartsWith(Program.ResultPrefix, StringComparison.Ordinal)))
            {
                return lines;
            }

            await Task.Delay(poll);
        }

        logger.LogError("The app did not finish its steps in {Minutes} minutes", patience.TotalMinutes);
        return lines;
    }

    // The system's picker is another app. It opens where it was last left, so the picture is looked for where it is,
    // and the roots list is opened to go to Downloads when it is not in sight.
    private async Task Pick(string serial)
    {
        var watch = Stopwatch.StartNew();
        var chosen = false;

        while (watch.Elapsed < pickPatience)
        {
            var nodes = await Screen(serial);

            if (nodes.Count is 0 || !nodes.Any(node => node.Package.Contains("documentsui", StringComparison.Ordinal)))
            {
                // Nothing read is a screen that was busy drawing; only a screen read without the picker says it closed.
                if (chosen && nodes.Count > 0)
                {
                    return;
                }

                await Task.Delay(poll);
                continue;
            }

            if (chosen && nodes.FirstOrDefault(node => node.Text is "Open" or "Select" or "OPEN" or "SELECT") is { } confirm)
            {
                await Tap(serial, confirm);
                return;
            }

            if (!chosen && nodes.FirstOrDefault(node => node.Text == PictureName) is { } file)
            {
                await Tap(serial, file);
                chosen = true;
            }
            else if (!chosen && nodes.FirstOrDefault(node => node.Text == "Downloads") is { } downloads)
            {
                await Tap(serial, downloads);
            }
            else if (!chosen && nodes.FirstOrDefault(node => node.Description == "Show roots") is { } roots)
            {
                await Tap(serial, roots);
            }

            await Task.Delay(poll);
        }

        logger.LogError("The picker never offered {Picture}; the last screen read is kept as the-last-screen.xml", PictureName);
    }

    // A finger goes down on the sandbox picture, holds, moves across it, and lifts: real touch events from the system,
    // as a person's finger makes them, not a call into the app.
    private async Task Drag(string serial, MemorySandboxProvider sandbox)
    {
        var watch = Stopwatch.StartNew();

        while (watch.Elapsed < dragPatience)
        {
            var nodes = await Screen(serial);

            if (nodes.FirstOrDefault(node => node.Description.Contains("Click or tap the desktop", StringComparison.Ordinal)) is { } picture)
            {
                var (left, top, width, height) = (picture.Left, picture.Top, picture.Right - picture.Left, picture.Bottom - picture.Top);
                var (screenWidth, screenHeight) = (nodes.Max(node => node.Right), nodes.Max(node => node.Bottom));

                // The device reports only what is on screen, so a picture that is not all there is cut short. It is
                // below the controls on a phone, so the page is scrolled by a finger that starts in its margin, where
                // the picture cannot take it.
                if (height < width * 0.7 || picture.Bottom > screenHeight * 0.88)
                {
                    await AndroidApp.Adb(serial, "shell", "input", "swipe", "8", $"{screenHeight * 7 / 10}", "8", $"{screenHeight * 4 / 10}", "500");
                    await Task.Delay(poll);
                    continue;
                }

                var (fromX, fromY) = (left + (width * 44 / 100), top + (height * 22 / 100));
                var (toX, toY) = (left + (width * 64 / 100), top + (height * 48 / 100));
                logger.LogInformation("Dragging from {FromX},{FromY} to {ToX},{ToY} over the sandbox picture", fromX, fromY, toX, toY);
                await AndroidApp.Adb(serial, "shell", "input", "draganddrop", $"{fromX}", $"{fromY}", $"{toX}", $"{toY}", "1500");
                await WaitForRelease(sandbox);
                return;
            }

            await Task.Delay(poll);
        }

        logger.LogError("The sandbox picture in control mode was not on the screen after {Seconds} seconds", dragPatience.TotalSeconds);
    }

    // The server is asked what it received, so the wait ends when the release has arrived, or after a bound.
    private static async Task WaitForRelease(MemorySandboxProvider sandbox)
    {
        var watch = Stopwatch.StartNew();

        while (watch.Elapsed < dragPatience && !PointerEvents.Of(sandbox.Commands).Any(pointer => pointer.Kind is PointerKind.Up))
        {
            await Task.Delay(poll);
        }
    }

    #endregion

    #region Judging

    private async Task Judge(CheckAccount account, MemorySandboxProvider sandbox, byte[] attachment, IReadOnlyList<string> lines)
    {
        Say(1, "chat: a message is sent and the bot's reply arrives", InApp(lines, 1));
        var stored = await account.StoredFile(PictureName);
        var uploaded = InApp(lines, 2);
        Say(2, "upload: the picture is attached and sent, and the server stored the same bytes", uploaded.Passed
            ? stored is null
                ? (false, $"the app said the picture went, but the server holds no file named {PictureName}")
                : Convert.ToHexStringLower(SHA256.HashData(stored)) == Convert.ToHexStringLower(SHA256.HashData(attachment))
                    ? (true, $"{attachment.Length} bytes sent, the same {stored.Length} bytes stored, sha256 {Convert.ToHexStringLower(SHA256.HashData(stored))[..16]}")
                    : (false, $"{attachment.Length} bytes were sent but the server stored {stored.Length} bytes that differ")
            : uploaded);
        var opened = InApp(lines, 3);
        Say(3, "sandbox: the screen opens with its picture and its mode line", opened.Passed && sandbox.Commands.Any(command => command.Program is "scrot")
            ? (true, "the picture was asked for and the mode line was shown")
            : (false, opened.Passed ? "the sandbox was never asked for its picture" : opened.Detail));
        var events = PointerEvents.Of(sandbox.Commands).ToArray();
        var held = PointerEvents.Held(events);
        var dragged = InApp(lines, 4);
        Say(4, "drag: a finger drag on the sandbox picture reaches the sandbox as a down, moves and an up",
            dragged.Passed && events is [{ Kind: PointerKind.Down }, .. { Length: >= 1 } moves, { Kind: PointerKind.Up }]
                && moves.All(pointer => pointer.Kind is PointerKind.Move) && held.Count is 0
                ? (true, $"down at {events[0].X},{events[0].Y}, {moves.Length} moves, up at {events[^1].X},{events[^1].Y}, no button left held")
                : (false, dragged.Passed
                    ? $"the sandbox recorded {Describe(events)}; a drag is a down, one or more moves and an up, and nothing left held"
                    : dragged.Detail));
    }

    private static string Describe(PointerEvent[] events) =>
        events.Length is 0
            ? "no pointer events"
            : $"{string.Join(", ", events.Select(pointer => pointer.Kind.ToString().ToLowerInvariant()))}{(PointerEvents.Held(events).Count > 0 ? " with a button still held" : string.Empty)}";

    // What the app said of one of its four steps: it passed, it failed and said why, or it never got there.
    private static (bool Passed, string Detail) InApp(IReadOnlyList<string> lines, int number)
    {
        var prefix = $"proof {number} ";

        if (lines.FirstOrDefault(line => line.StartsWith($"ui-check pass: {prefix}", StringComparison.Ordinal)) is { } pass)
        {
            return (true, pass["ui-check pass: ".Length..]);
        }

        return lines.FirstOrDefault(line => line.StartsWith($"ui-check fail: {prefix}", StringComparison.Ordinal)) is { } fail
            ? (false, fail["ui-check fail: ".Length..])
            : (false, "the app never reached this step");
    }

    private void Say(int number, string what, (bool Passed, string Detail) verdict)
    {
        verdicts.Add(($"{number}/4 {what}", verdict.Passed, verdict.Detail));
        logger.LogInformation("PROOF {Verdict} {Number}/4 {What}: {Detail}", verdict.Passed ? "PASS" : "FAIL", number, what, verdict.Detail);
    }

    #endregion

    #region The Device

    private async Task<IReadOnlyList<ScreenNode>> Screen(string serial)
    {
        try
        {
            // The dump fails while the app is drawing; the next look is a second away.
            await AndroidApp.Adb(serial, "shell", "uiautomator", "dump", DeviceScreen);
            lastScreen = await AndroidApp.Adb(serial, "exec-out", "cat", DeviceScreen);
            return [.. XDocument.Parse(lastScreen).Descendants("node").Select(ScreenNode.From)];
        }
        catch (InvalidOperationException failure)
        {
            logger.LogDebug(failure, "The screen could not be read this time");
            return [];
        }
    }

    private static async Task Tap(string serial, ScreenNode node) =>
        await AndroidApp.Adb(serial, "shell", "input", "tap", $"{(node.Left + node.Right) / 2}", $"{(node.Top + node.Bottom) / 2}");

    private static Process StartRecording(string serial)
    {
        var start = new ProcessStartInfo("adb") { RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (var argument in new[] { "-s", serial, "shell", "screenrecord", "--time-limit", "180", "--size", "540x1200", DeviceRecording })
        {
            start.ArgumentList.Add(argument);
        }

        return Process.Start(start) ?? throw new InvalidOperationException("adb did not start the recording.");
    }

    // A failed run is read from what is kept, so everything is kept whether the run passed or not. Keeping is best
    // effort: a recording that cannot be pulled must not hide the verdict.
    private async Task Collect(string serial, string runId, Process recording)
    {
        await Keep("the final screenshot", async () =>
        {
            var shot = new ProcessStartInfo("adb") { RedirectStandardOutput = true };

            foreach (var argument in new[] { "-s", serial, "exec-out", "screencap", "-p" })
            {
                shot.ArgumentList.Add(argument);
            }

            using var screencap = Process.Start(shot) ?? throw new InvalidOperationException("adb did not start.");
            await using var file = File.Create(Path.Combine(artifacts, "final-screen.png"));
            await screencap.StandardOutput.BaseStream.CopyToAsync(file);
            await screencap.WaitForExitAsync();
        });
        await Keep("the device log", async () =>
        {
            await File.WriteAllTextAsync(Path.Combine(artifacts, "logcat-tail.txt"), await AndroidApp.Adb(serial, "logcat", "-d", "-v", "epoch", "-t", "4000"));
            var tagged = await AndroidApp.Adb(serial, "logcat", "-d", "-v", "epoch", "-s", $"{AndroidApp.Tag}:*");
            await File.WriteAllLinesAsync(Path.Combine(artifacts, "app-report.txt"), AndroidCheckGuard.TagLogsForRun(tagged, runId, AndroidApp.Tag));
        });
        await Keep("the last screen read", () => File.WriteAllTextAsync(Path.Combine(artifacts, "the-last-screen.xml"), lastScreen));
        await Keep("the screen recording", async () =>
        {
            await AndroidApp.Adb(serial, "shell", "pkill", "-INT", "screenrecord");
            using var finishing = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await recording.WaitForExitAsync(finishing.Token);
            await AndroidApp.Adb(serial, "pull", DeviceRecording, Path.Combine(artifacts, "proof.mp4"));
        });
    }

    private async Task Keep(string what, Func<Task> keep)
    {
        try
        {
            await keep();
        }
        catch (Exception failure) when (failure is InvalidOperationException or IOException or OperationCanceledException)
        {
            logger.LogWarning(failure, "Could not keep {What}", what);
        }
    }

    #endregion

    #region Nested Types

    /// <summary>One element of the screen the device's accessibility service reports, with its box in pixels.</summary>
    private sealed record ScreenNode(string Package, string Text, string Description, int Left, int Top, int Right, int Bottom)
    {
        public static ScreenNode From(XElement node)
        {
            var box = System.Text.RegularExpressions.Regex.Matches(node.Attribute("bounds")?.Value ?? string.Empty, @"\d+")
                .Select(number => int.Parse(number.Value, CultureInfo.InvariantCulture))
                .ToArray();

            return new ScreenNode(
                node.Attribute("package")?.Value ?? string.Empty,
                node.Attribute("text")?.Value ?? string.Empty,
                node.Attribute("content-desc")?.Value ?? string.Empty,
                box.ElementAtOrDefault(0),
                box.ElementAtOrDefault(1),
                box.ElementAtOrDefault(2),
                box.ElementAtOrDefault(3));
        }
    }

    #endregion
}
