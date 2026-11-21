namespace Drover.Ui.Tests.Support;

/// <summary>
/// The one place the browser tests find a browser. Environment and a file-exists function are parameters so the
/// order, the system's paths and the error can be tested without a browser on the machine.
/// </summary>
public static class TestBrowser
{
    #region Public Methods

    /// <summary>The executable to hand to Playwright, or one exception listing every path tried.</summary>
    public static string Path() => Path(Environment.GetEnvironmentVariable, File.Exists);

    /// <summary>The same lookup with injectable inputs. <paramref name="exists"/> sees paths, not URLs.</summary>
    public static string Path(Func<string, string?> environment, Func<string, bool> exists) =>
        Path(environment, exists, CurrentSystem());

    internal static string Path(Func<string, string?> environment, Func<string, bool> exists, TestBrowserOS system)
    {
        string[] variables = [ChosenVariable, PlaywrightVariable];

        foreach (var variable in variables)
        {
            if (environment(variable) is { Length: > 0 } chosen)
            {
                return exists(chosen)
                    ? chosen
                    : throw new TestEnvironmentException($"{variable} is set to {chosen}, which is not a file. Correct the variable.");
            }
        }

        List<string> tried = [.. FixedPaths(system)];

        foreach (var path in tried)
        {
            if (exists(path))
            {
                return path;
            }
        }

        var downloaded = Downloaded(environment, exists, system);

        if (downloaded is not null)
        {
            return downloaded;
        }

        tried.Add("Playwright's downloaded Chromium folder");
        var list = string.Join('\n', tried.Select(path => $"  {path}"));

        throw new TestEnvironmentException(
            $"""
            No Chromium, Chrome or Edge was found for the browser tests. Looked at:
            {list}
            Fix one of: install Chrome; run `pwsh <test output folder>/playwright.ps1 install chromium`; or set {ChosenVariable} to a browser's executable.
            """);
    }

    /// <summary>The fixed paths for this system, in lookup order. Also used by the error message.</summary>
    public static List<string> FixedPaths() => FixedPaths(CurrentSystem());

    internal static List<string> FixedPaths(TestBrowserOS system)
    {
        if (system is TestBrowserOS.MacOS)
        {
            List<string> mac = [];
            string[] homes = ["/Applications", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)];

            foreach (var home in homes)
            {
                var folder = System.IO.Path.GetFileName(home) == "Applications"
                    ? home
                    : System.IO.Path.Combine(home, "Applications");
                mac.Add(System.IO.Path.Combine(folder, "Google Chrome.app/Contents/MacOS/Google Chrome"));
                mac.Add(System.IO.Path.Combine(folder, "Chromium.app/Contents/MacOS/Chromium"));
                mac.Add(System.IO.Path.Combine(folder, "Microsoft Edge.app/Contents/MacOS/Microsoft Edge"));
            }

            return mac;
        }

        if (system is TestBrowserOS.Windows)
        {
            List<string> windows = [];

            foreach (var root in Roots().Where(root => root.Length > 0))
            {
                windows.Add(System.IO.Path.Combine(root, "Google/Chrome/Application/chrome.exe"));
                windows.Add(System.IO.Path.Combine(root, "Microsoft/Edge/Application/msedge.exe"));
            }

            return windows;
        }

        return ["/opt/pw-browsers/chromium", "/usr/bin/google-chrome", "/usr/bin/chromium", "/usr/bin/chromium-browser"];
    }

    #endregion

    #region Private Methods

    private static TestBrowserOS CurrentSystem() =>
        OperatingSystem.IsMacOS() ? TestBrowserOS.MacOS
            : OperatingSystem.IsWindows() ? TestBrowserOS.Windows
            : TestBrowserOS.Linux;

    private static List<string> Roots() =>
    [
        Environment.GetEnvironmentVariable("ProgramFiles") ?? string.Empty,
        Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? string.Empty,
        Environment.GetEnvironmentVariable("LocalAppData") ?? string.Empty,
    ];

    private static string DefaultDownloadRoot(TestBrowserOS system) =>
        system switch
        {
            TestBrowserOS.MacOS => System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Caches/ms-playwright"),
            TestBrowserOS.Windows => System.IO.Path.Combine(
                Environment.GetEnvironmentVariable("LocalAppData") ?? string.Empty, "ms-playwright"),
            _ => System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache/ms-playwright"),
        };

    private static string[] ExecutablePaths(TestBrowserOS system) =>
        system switch
        {
            TestBrowserOS.MacOS =>
            [
                "chrome-mac/Chromium.app/Contents/MacOS/Chromium",
                "chrome-mac-arm64/Chromium.app/Contents/MacOS/Chromium",
                "chrome-mac-64/Chromium.app/Contents/MacOS/Chromium",
            ],
            TestBrowserOS.Windows => ["chrome-win/chrome.exe", "chrome-win64/chrome.exe"],
            _ => ["chrome-linux/chrome", "chrome-linux64/chrome"],
        };

    private static int Revision(string folder)
    {
        var name = System.IO.Path.GetFileName(folder);
        var digits = name.StartsWith("chromium-", StringComparison.Ordinal) ? name["chromium-".Length..] : string.Empty;
        return int.TryParse(digits, out var number) ? number : -1;
    }

    private static string? Downloaded(Func<string, string?> environment, Func<string, bool> exists, TestBrowserOS system)
    {
        var root = environment("PLAYWRIGHT_BROWSERS_PATH");

        if (string.IsNullOrWhiteSpace(root))
        {
            root = DefaultDownloadRoot(system);
        }

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return null;
        }

        var candidates = ExecutablePaths(system);

        return Directory.GetDirectories(root, "chromium-*")
            .OrderByDescending(Revision)
            .SelectMany(folder => candidates.Select(relative => System.IO.Path.Combine(folder, relative)))
            .Where(exists)
            .FirstOrDefault();
    }

    #endregion

    #region Constants

    private const string ChosenVariable = "DROVER_TEST_CHROMIUM";

    private const string PlaywrightVariable = "PLAYWRIGHT_CHROMIUM_EXECUTABLE";

    #endregion

    #region Nested Types

    /// <summary>Which system layout a test is checking. The shipped lookup derives this from the runtime.</summary>
    public enum TestBrowserOS
    {
        Linux,
        MacOS,
        Windows,
    }

    #endregion
}
