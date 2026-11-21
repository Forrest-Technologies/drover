namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// Email and password fields carry the same autofill hints on Android and desktop, and a field refuses text past
/// its limit without raising an error.
/// </summary>
public sealed class FieldInputTests
{
    #region Public Methods

    [Fact]
    public void Email_and_password_fields_carry_android_and_desktop_hints()
    {
        FieldInput.AndroidHint("email", "username").Should().Be("username");
        FieldInput.AndroidHint("password", "current-password").Should().Be("password");
        FieldInput.AndroidHint("password", "new-password").Should().Be("newPassword");
        FieldInput.AndroidHint("text", null).Should().BeNull();
        FieldInput.DesktopScope("email").Should().Be("EmailSmtpAddress");
        FieldInput.DesktopScope("password").Should().Be("Password");
        FieldInput.DesktopScope("url").Should().Be("Url");

        var field = Repository.NativeFiles(".cs").Single(file => file.Path == "Controls/Field.cs").Text;
        field.Should().Contain("FieldInput.AndroidHint");
        field.Should().Contain("FieldInput.DesktopScope");
        field.Should().Contain("FieldInput.Within");
    }

    [Fact]
    public void Typing_past_a_limit_is_cut_and_does_not_throw()
    {
        var over = new string('n', FieldLimits.DisplayName + 5);

        var cut = FieldInput.Within(over, FieldLimits.DisplayName);

        cut.Should().Be(new string('n', FieldLimits.DisplayName));
        FieldInput.Within("kept", FieldLimits.DisplayName).Should().Be("kept");
        FieldInput.Within(over, 0).Should().Be(over, "a field with no limit keeps what was typed");
    }

    [Fact]
    public void Shipping_builds_omit_field_input_check_and_launch_hooks()
    {
        var nativeRoot = Path.Combine(Repository.Root, "src", "Drover.Native");
        var project = File.ReadAllText(Path.Combine(nativeRoot, "Drover.Native.csproj"));
        var activity = File.ReadAllText(Path.Combine(nativeRoot, "Platforms", "Android", "MainActivity.cs"));
        var app = File.ReadAllText(Path.Combine(nativeRoot, "App.xaml.cs"));
        var fieldsScript = File.ReadAllText(Path.Combine(nativeRoot, "Platforms", "WebAssembly", "WasmScripts", "Fields.js"));
        var workflow = File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", "native.yml"));

        project.Should().Contain("""<PropertyGroup Condition="'$(DroverFieldCheck)' == 'true'">""");
        project.Should().Contain("""<ItemGroup Condition="'$(DroverFieldCheck)' != 'true'">""");
        project.Should().Contain("""<Compile Remove="Controls/FieldInputCheck.cs" />""");
        project.Should().Contain("""<Compile Remove="Platform/NativeStop.cs" />""");

        activity.Should().Contain("#if DROVER_FIELD_CHECK");
        app.Should().Contain("#if DROVER_FIELD_CHECK");
        fieldsScript.Should().Contain("current.count");
        workflow.Should().Contain("DroverFieldCheck=true");
        workflow.Should().Contain("drover\\.field_input");
    }

    #endregion
}
