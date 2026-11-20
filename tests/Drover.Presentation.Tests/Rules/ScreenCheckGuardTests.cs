namespace Drover.Presentation.Tests.Rules;

/// <summary>
/// The screen-check harness wipes an Android package only when the run names that device and the package is the
/// checks build, and a missing automation id is not a match.
/// </summary>
public sealed partial class ScreenCheckGuardTests
{
    #region Public Methods

    [Fact]
    public void A_phone_that_is_not_the_checks_build_is_not_cleared()
    {
        const string Devices = "List of devices attached\nemulator-5554\tdevice\n";

        AndroidCheckGuard.Serials(Devices).Should().Equal("emulator-5554");
        AndroidCheckGuard.Choose(["emulator-5554"], "emulator-5554", AndroidCheckGuard.ChecksVersion)
            .Should().Be("emulator-5554");
        AndroidCheckGuard.Choose(["emulator-5554"], null, AndroidCheckGuard.ChecksVersion).Should().BeNull();
        AndroidCheckGuard.Choose(["emulator-5554"], "emulator-5554", "0.1.0").Should().BeNull();
        AndroidCheckGuard.Choose(["R58M"], "R58M", "0.1.0").Should().BeNull();
        AndroidCheckGuard.Choose(["R58M"], "R58M", AndroidCheckGuard.ChecksVersion).Should().Be("R58M");
        AndroidCheckGuard.Choose(["R58M", "emulator-5554"], null, null).Should().BeNull();
        AndroidCheckGuard.Choose(["emulator-5554"], "missing", AndroidCheckGuard.ChecksVersion).Should().BeNull();
        AndroidCheckGuard.VersionName("    versionName=checks\n").Should().Be("checks");
    }

    [Fact]
    public void A_report_from_before_this_run_is_not_the_result()
    {
        const string Run = "run-current";
        const string Log = """
            1700000000.100  1  2 I DroverUiCheck: run-previous ui-check result: passed
            1700000000.150  1  2 I Other: run-current ui-check result: passed
            1700000000.200  1  2 I DroverUiCheck: run-current ui-check pass: launch (100 ms)
            1700000000.900  1  2 I DroverUiCheck: run-current ui-check result: passed
            """;

        AndroidCheckGuard.ReportsForRun(Log, Run).Should().Equal(
            "ui-check pass: launch (100 ms)",
            "ui-check result: passed");
        AndroidCheckGuard.TagLogsForRun(Log, Run, "DroverUiCheck").Should().Equal(
            "1700000000.200  1  2 I DroverUiCheck: run-current ui-check pass: launch (100 ms)",
            "1700000000.900  1  2 I DroverUiCheck: run-current ui-check result: passed");
    }

    [Fact]
    public void The_checks_build_is_a_separate_package_and_keeps_its_own_settings()
    {
        var project = File.ReadAllText(Path.Combine(Repository.Root, "src", "Drover.Native", "Drover.Native.csproj"));
        var shims = File.ReadAllText(Path.Combine(
            Repository.Root,
            "src",
            "Drover.Native",
            "Device",
            "StorageShims.cs"));
        var hook = File.ReadAllText(Path.Combine(
            Repository.Root,
            "tests",
            "Drover.Native.UITests",
            "App",
            "PreferencesHook.cs"));
        var android = File.ReadAllText(Path.Combine(
            Repository.Root,
            "tests",
            "Drover.Native.UITests",
            "AndroidApp.cs"));
        var workflow = File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", "native.yml"));

        project.Should().Contain(
            """<ApplicationId Condition="'$(DroverUiChecks)' == 'true'">com.forresttechnologies.drover.checks</ApplicationId>""");
        shims.Should().NotContain("DROVER_UI_CHECK_REPORT");
        shims.Should().NotContain("checks-preferences.txt");
        hook.Should().Contain("DROVER_UI_CHECK_REPORT");
        hook.Should().Contain("checks-preferences.txt");
        android.Should().Contain("com.forresttechnologies.drover.checks");
        android.Should().NotContain("logcat\", \"-c\"");
        android.Should().NotContain("logcat\", \"-d\")");
        android.Should().Contain("TagLogsForRun");
        android.Should().Contain("DROVER_ANDROID_SERIAL");
        workflow.Should().Contain("DROVER_ANDROID_SERIAL");
        workflow.Should().Contain("adb -s \"${serial}\" install -r");
        workflow.Should().Contain("grep -a -q DROVER_UI_CHECK_REPORT");
        workflow.IndexOf("*-Signed.apk", StringComparison.Ordinal).Should().BeLessThan(
            workflow.IndexOf("DROVER_ANDROID_SERIAL", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_automation_id_fails_the_screen_check_lookup()
    {
        AutomationIds.Matches("SignInEmail", shown: true, "SignInEmail").Should().BeTrue();
        AutomationIds.Matches("SignInEmail", shown: true, "SignInEmailMissing").Should().BeFalse();
        AutomationIds.Matches("SignInEmail", shown: false, "SignInEmail").Should().BeFalse();

        var checks = File.ReadAllText(Path.Combine(
            Repository.Root,
            "tests",
            "Drover.Native.UITests",
            "App",
            "ScreenChecks.cs"));
        var wanted = AutomationIdCall().Matches(checks).Select(match => match.Groups[1].Value).Distinct().ToList();
        var native = string.Join('\n', Repository.NativeFiles(".cs", ".xaml").Select(file => file.Text));

        wanted.Should().NotBeEmpty();
        var project = File.ReadAllText(Path.Combine(Repository.Root, "src", "Drover.Native", "Drover.Native.csproj"));
        project.Should().Contain("""<ItemGroup Condition="'$(DroverUiChecks)' == 'true'">""");
        project.Should().NotContain("DroverUiChecks)' == 'true' and");
        checks.Should().NotContain("DROVER_BROWSER");
        checks.Should().Contain("DispatcherQueue.CreateTimer()");
        checks.Should().NotContain("Task.Delay");
        foreach (var id in wanted)
        {
            native.Should().Contain($"\"{id}\"", $"{id} is a control the screen checks press");
            AutomationIds.Matches(null, shown: true, id).Should().BeFalse();
        }
    }

    #endregion

    #region Private Methods

    [GeneratedRegex(@"(?:Shown|Type|Press)\(""([^""]+)""")]
    private static partial Regex AutomationIdCall();

    #endregion
}
