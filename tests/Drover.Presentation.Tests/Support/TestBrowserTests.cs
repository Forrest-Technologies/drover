namespace Drover.Presentation.Tests.Support;

/// <summary>The browser lookup is pure: the environment and a file-exists function decide everything.</summary>
public sealed class TestBrowserTests
{
    #region Public Methods

    [Fact]
    public void The_first_variable_wins()
    {
        var path = TestBrowser.Path(
            variable => variable is "DROVER_TEST_CHROMIUM" ? "/chosen/chrome" : "/other/chrome",
            _ => true);

        path.Should().Be("/chosen/chrome");
    }

    [Fact]
    public void The_playwright_variable_is_used_when_the_first_is_unset()
    {
        var path = TestBrowser.Path(
            variable => variable is "PLAYWRIGHT_CHROMIUM_EXECUTABLE" ? "/downloaded/chrome" : null,
            _ => true);

        path.Should().Be("/downloaded/chrome");
    }

    [Fact]
    public void A_set_variable_that_is_not_a_file_names_the_variable()
    {
        var failure = FluentActions.Invoking(() => TestBrowser.Path(
                variable => variable is "DROVER_TEST_CHROMIUM" ? "/missing/chrome" : null,
                _ => false))
            .Should().Throw<TestEnvironmentException>();

        failure.Which.Message.Should().Contain("DROVER_TEST_CHROMIUM").And.Contain("/missing/chrome");
    }

    [Fact]
    public void The_fixed_paths_for_this_system_are_tried_before_the_download_folder()
    {
        var paths = TestBrowser.FixedPaths();
        paths.Should().NotBeEmpty();

        var path = TestBrowser.Path(_ => null, candidate => candidate == paths[1]);

        path.Should().Be(paths[1]);
    }

    [Theory]
    [InlineData(TestBrowser.TestBrowserOS.Linux, "chromium-200/chrome-linux/chrome", "chromium-100/chrome-linux/chrome")]
    [InlineData(TestBrowser.TestBrowserOS.MacOS, "chromium-200/chrome-mac-arm64/Google Chrome for Testing.app/Contents/MacOS/Google Chrome for Testing", "chromium-100/chrome-mac-arm64/Google Chrome for Testing.app/Contents/MacOS/Google Chrome for Testing")]
    [InlineData(TestBrowser.TestBrowserOS.MacOS, "chromium-200/chrome-mac/Chromium.app/Contents/MacOS/Chromium", "chromium-100/chrome-mac/Chromium.app/Contents/MacOS/Chromium")]
    [InlineData(TestBrowser.TestBrowserOS.Windows, "chromium-200/chrome-win/chrome.exe", "chromium-100/chrome-win/chrome.exe")]
    public void The_newest_downloaded_chromium_is_chosen_for_each_system(
        TestBrowser.TestBrowserOS system,
        string newerRelative,
        string olderRelative)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"drover-browsers-{Guid.NewGuid():N}");
        var newer = System.IO.Path.Combine(root, newerRelative);
        var older = System.IO.Path.Combine(root, olderRelative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(newer)!);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(older)!);

        try
        {
            var path = TestBrowser.Path(
                variable => variable is "PLAYWRIGHT_BROWSERS_PATH" ? root : null,
                candidate => candidate == newer || candidate == older,
                system);

            path.Should().Be(newer);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_four_digit_revision_beats_a_smaller_one()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"drover-browsers-{Guid.NewGuid():N}");
        var newest = System.IO.Path.Combine(root, "chromium-1000/chrome-linux/chrome");
        var older = System.IO.Path.Combine(root, "chromium-999/chrome-linux/chrome");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(newest)!);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(older)!);

        try
        {
            var path = TestBrowser.Path(
                variable => variable is "PLAYWRIGHT_BROWSERS_PATH" ? root : null,
                candidate => candidate == newest || candidate == older,
                TestBrowser.TestBrowserOS.Linux);

            path.Should().Be(newest);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_error_lists_this_systems_paths_and_the_three_fixes()
    {
        var tried = TestBrowser.FixedPaths();
        var failure = FluentActions.Invoking(() => TestBrowser.Path(_ => null, _ => false))
            .Should().Throw<TestEnvironmentException>().Which;

        failure.Message.Should().Contain("No Chromium, Chrome or Edge was found for the browser tests.");

        foreach (var path in tried)
        {
            failure.Message.Should().Contain(path);
        }

        failure.Message.Should().Contain("install Chrome")
            .And.Contain("playwright.ps1 install chromium")
            .And.Contain("DROVER_TEST_CHROMIUM");
    }

    #endregion
}
