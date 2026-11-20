namespace Drover.Native.Checks;

/// <summary>
/// How a screen check recognises a control. A missing id, or one that is not on screen, is not a match, so the
/// check fails instead of pressing whatever else is there.
/// </summary>
internal static class AutomationIds
{
    #region Public Methods

    public static bool Matches(string? automationId, bool shown, string wanted) =>
        shown && automationId == wanted;

    #endregion
}
