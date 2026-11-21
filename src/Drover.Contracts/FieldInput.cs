namespace Drover.Contracts;

/// <summary>
/// How a native field tells the platform what it is, and how it refuses text past its limit. Android, desktop, and
/// the browser read the same answers, so a hint cannot exist on only one of them.
/// </summary>
public static class FieldInput
{
    #region Public Methods

    /// <summary>The Android autofill hint for a field, or null when Android should not offer to fill it.</summary>
    public static string? AndroidHint(string? type, string? autofill) => (type, autofill) switch
    {
        ("password", "current-password") => "password",
        ("password", "new-password") => "newPassword",
        (_, "username") => "username",
        _ => null,
    };

    /// <summary>
    /// The desktop input scope name for a field type. Password, email, and URL are the ones the system fills.
    /// </summary>
    public static string DesktopScope(string? type) => type switch
    {
        "password" => "Password",
        "email" => "EmailSmtpAddress",
        "url" => "Url",
        _ => "Default",
    };

    /// <summary>
    /// The text a field keeps when its limit is <paramref name="maxLength"/> characters. Zero means the field has
    /// no limit. A limit never cuts through a character that occupies two UTF-16 code units.
    /// </summary>
    public static string Within(string text, int maxLength)
    {
        if (maxLength <= 0 || text.Length <= maxLength)
        {
            return text;
        }

        var end = maxLength;
        if (char.IsHighSurrogate(text[end - 1]))
        {
            end--;
        }

        return text[..end];
    }

    #endregion
}
