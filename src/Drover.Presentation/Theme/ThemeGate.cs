namespace Drover.Ui.Theme;

/// <summary>The accessibility gates every theme must pass, in both modes. See <see cref="ThemeGates"/>.</summary>
public enum ThemeGate
{
    /// <summary>Every text and status color reaches 4.5:1 on every surface, and so does text on the accent.</summary>
    TextContrast,

    /// <summary>Control edges and the focus ring reach 3:1 on every surface.</summary>
    InterfaceContrast,

    /// <summary>
    /// Each of the twelve hues reaches 4.5:1 on every surface, and each head 3:1 against the visor at rest, lit and
    /// failed.
    /// </summary>
    BotHues,

    /// <summary>Every value of the file is inside the bounds of the theme-file format.</summary>
    Bounds,

    /// <summary>
    /// Control and row heights at the theme's density still give 44 for a finger and 32 for a pointer.
    /// </summary>
    TouchTargets,

    /// <summary>The accent roles are present and distinct, and a theme defines no gradient and no shadow.</summary>
    Invariants,
}

/// <summary>
/// One thing a theme gets wrong. It names the theme, the mode when the failure is in one, the gate, and the pair or
/// the value that fails with its measure, so the person fixing the theme does not have to search.
/// </summary>
public sealed record GateFailure(string ThemeId, ColorMode? Mode, ThemeGate Gate, string What)
{
    public override string ToString()
    {
        var mode = Mode is { } named ? $", {named.ToString().ToLowerInvariant()}" : string.Empty;

        return $"{ThemeId}{mode}: {Gate}: {What}";
    }
}
