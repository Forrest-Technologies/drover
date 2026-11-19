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

    #endregion
}
