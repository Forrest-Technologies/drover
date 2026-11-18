namespace Drover.Presentation.ViewModels;

/// <summary>One theme in the account menu: its words, and whether it is the one in use.</summary>
public sealed record ThemeOption(ThemeChoice Choice, string Name, bool InUse)
{
    /// <summary>What a screen reader is told: the name, and that it is the one in use when it is.</summary>
    public string AccessibleName => InUse ? $"{Name}, in use" : Name;
}
