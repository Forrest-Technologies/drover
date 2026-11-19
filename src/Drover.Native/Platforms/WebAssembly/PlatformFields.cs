namespace Drover.Native.Platform;

/// <summary>
/// In a browser a password manager reads the page's inputs. The platform keeps one for each field it draws, in the
/// accessibility tree, named by the field's label, and that is where a field's <c>autocomplete</c> goes.
/// </summary>
internal static partial class PlatformFields
{
    #region Public Methods

    /// <summary>Names what the field with this label holds while it is on screen, or stops naming it with null.</summary>
    public static void Name(string label, string? autofill) => SetNamed(label, autofill);

    #endregion

    #region Private Methods

    [JSImport("globalThis.droverFields.name")]
    private static partial void SetNamed(string label, string? autofill);

    #endregion
}
