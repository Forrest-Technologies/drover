namespace Drover.Ui.Tests.Support;

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

    [Fact]
    public void The_newest_downloaded_chromium_is_chosen()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"drover-browsers-{Guid.NewGuid():N}");
        var older = System.IO.Path.Combine(root, "chromium-100", "chrome-linux", "chrome");
        var newer = System.IO.Path.Combine(root, "chromium-200", "chrome-linux", "chrome");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(older)!);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(newer)!);

        try
        {
            var path = TestBrowser.Path(
                variable => variable is "PLAYWRIGHT_BROWSERS_PATH" ? root : null,
                candidate => candidate == older || candidate == newer);

            path.Should().Be(newer);
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
