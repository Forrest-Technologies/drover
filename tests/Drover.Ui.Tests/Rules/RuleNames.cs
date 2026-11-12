namespace Drover.Ui.Tests.Rules;

/// <summary>
/// The names rules are reported under, and the names an allow comment may use.
/// </summary>
public static class RuleNames
{
    #region Properties

    public const string NoRawValues = "no-raw-values";

    public const string NoEmoji = "no-emoji";

    public const string MotionOnlyForLiveState = "motion-only-for-live-state";

    public const string ReducedMotionHonored = "reduced-motion-honored";

    public const string NoViewportHeight = "no-100vh";

    public const string FocusAlwaysVisible = "focus-always-visible";

    public const string ControlsAreRealControls = "controls-are-real-controls";

    public const string IconOnlyControlsAreNamed = "icon-only-controls-are-named";

    public const string FieldsAreLabeled = "fields-are-labeled";

    public const string MediaHasDimensions = "media-has-dimensions";

    public const string NoHypeWords = "no-hype-words";

    /// <summary>
    /// Reported for an allow comment that is malformed, names no rule, or allows nothing. It cannot be allowed.
    /// </summary>
    public const string AllowComment = "allow-comment";

    public static IReadOnlyList<string> Allowable { get; } =
    [
        NoRawValues,
        NoEmoji,
        MotionOnlyForLiveState,
        ReducedMotionHonored,
        NoViewportHeight,
        FocusAlwaysVisible,
        ControlsAreRealControls,
        IconOnlyControlsAreNamed,
        FieldsAreLabeled,
        MediaHasDimensions,
        NoHypeWords,
    ];

    #endregion
}
