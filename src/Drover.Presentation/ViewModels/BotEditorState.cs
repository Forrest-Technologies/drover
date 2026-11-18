namespace Drover.Presentation.ViewModels;

/// <summary>What the bot editor shows: one of these at a time.</summary>
public enum BotEditorState
{
    Loading,
    Failed,
    NotPermitted,
    Missing,
    Shown,
}
