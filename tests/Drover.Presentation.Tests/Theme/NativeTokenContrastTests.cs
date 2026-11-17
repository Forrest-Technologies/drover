namespace Drover.Presentation.Tests.Theme;

/// <summary>
/// Recomputes the contrast DESIGN.md promises from the values in the native client's Tokens.xaml, for both themes:
/// the same checks the web client's tokens are held to.
/// </summary>
public sealed class NativeTokenContrastTests
{
    #region Private Fields

    private const double TextMinimum = 4.5;

    private const double BorderMinimum = 3;

    private static readonly string[] surfaces = ["DvBg0", "DvBg1", "DvBg2", "DvBg3"];

    private static readonly string[] textAndStatus =
    [
        "DvText", "DvText2", "DvText3", "DvEmber", "DvIon", "DvViolet", "DvOk", "DvWarn", "DvDanger",
        .. Enumerable.Range(0, 12).Select(step => $"DvHue{step}"),
    ];

    private static readonly Dictionary<string, Dictionary<string, string>> themes = new()
    {
        ["dark"] = NativeTokens.Colors("Default"),
        ["light"] = NativeTokens.Colors("Light"),
    };

    #endregion

    #region Public Methods

    [Fact]
    public void Every_text_and_status_color_clears_4_5_to_1_on_every_surface_in_both_themes() =>
        Failures(textAndStatus, surfaces, TextMinimum).Should().BeEmpty();

    [Fact]
    public void The_control_border_clears_3_to_1_on_every_surface_in_both_themes() =>
        Failures(["DvText3"], surfaces, BorderMinimum).Should().BeEmpty();

    [Fact]
    public void Text_on_the_accent_clears_4_5_to_1_in_both_themes() =>
        Failures(["DvOnAccent"], ["DvEmber"], TextMinimum).Should().BeEmpty();

    [Fact]
    public void A_bots_visor_clears_3_to_1_against_every_head_at_rest_lit_and_failed_in_both_themes() =>
        Failures(
            ["DvVisor", "DvVisorLit", "DvVisorFailed"],
            [.. Enumerable.Range(0, 12).Select(step => $"DvHead{step}")],
            BorderMinimum).Should().BeEmpty();

    #endregion

    #region Private Methods

    private static List<string> Failures(string[] foregrounds, string[] backgrounds, double minimum) =>
        [.. from theme in themes
            from foreground in foregrounds
            from background in backgrounds
            let ratio = Contrast.Ratio(theme.Value[foreground], theme.Value[background])
            where ratio < minimum
            select $"{foreground} on {background} in the {theme.Key} theme is {ratio:0.00}:1, below {minimum}:1"];

    #endregion
}
