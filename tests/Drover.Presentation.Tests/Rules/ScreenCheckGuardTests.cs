namespace Drover.Presentation.Tests.Rules;

/// <summary>
/// The screen-check harness wipes an Android package only on an emulator or a checks build, and a missing
/// automation id is not a match.
/// </summary>
public sealed partial class ScreenCheckGuardTests
{
    #region Public Methods

    [Fact]
    public void A_phone_that_is_not_the_checks_build_is_not_cleared()
    {
        const string Devices = "List of devices attached\nemulator-5554\tdevice\n";

        AndroidCheckGuard.Serials(Devices).Should().Equal("emulator-5554");
        AndroidCheckGuard.Choose(["emulator-5554"], null).Should().Be("emulator-5554");
        AndroidCheckGuard.Choose(["R58M"], "0.1.0").Should().BeNull();
        AndroidCheckGuard.Choose(["R58M"], AndroidCheckGuard.ChecksVersion).Should().Be("R58M");
        AndroidCheckGuard.Choose(["R58M", "emulator-5554"], null).Should().Be("emulator-5554");
        AndroidCheckGuard.VersionName("    versionName=checks\n").Should().Be("checks");
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
        checks.Should().Contain("#if DROVER_BROWSER");
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
