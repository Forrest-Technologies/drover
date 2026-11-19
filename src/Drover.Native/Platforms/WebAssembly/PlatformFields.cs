namespace Drover.Native.Platform;

/// <summary>
/// In a browser every field types through one hidden input that follows the focus. A password manager reads that
/// input, so it is told what the focused field holds each time the focus moves.
/// </summary>
internal static partial class PlatformFields
{
    #region Public Methods

    /// <summary>Names what the focused field holds, as the input's <c>autocomplete</c>, or clears it with null.</summary>
    public static void Describe(string? autofill) => SetAutofill(autofill);

    #endregion

    #region Private Methods

    [JSImport("globalThis.droverFields.describe")]
    private static partial void SetAutofill(string? autofill);

    #endregion
}
