namespace Drover.Presentation.ViewModels;

/// <summary>One mode in the appearance choices: its words, and whether it is the one in use.</summary>
public sealed record ThemeOption(ThemeMode Mode, string Name, bool InUse)
{
    /// <summary>What a screen reader is told: the name, and that it is the one in use when it is.</summary>
    public string AccessibleName => InUse ? $"{Name}, in use" : Name;
}

/// <summary>One theme in the appearance choices: its name, what it is for, and whether it is the one in use.</summary>
public sealed record LookOption(string Id, string Name, string Intent, bool InUse)
{
    /// <summary>The extension that offers the theme, or null for one of the app's own.</summary>
    public string? Source { get; init; }

    /// <summary>What a screen reader is told: the name and what the theme is for, and that it is in use when it is.</summary>
    public string AccessibleName => InUse ? $"{Name}, in use. {Intent}" : $"{Name}. {Intent}";
}

/// <summary>
/// One accent in the appearance choices: its name, the color it gives a person's intent in the dark and the light mode of
/// the theme in use, and whether it is the one in use.
/// </summary>
public sealed record AccentOption(string Id, string Name, Rgb Dark, Rgb Light, bool InUse)
{
    /// <summary>What a screen reader is told: the name, and that it is the one in use when it is.</summary>
    public string AccessibleName => InUse ? $"{Name}, in use" : Name;
}
