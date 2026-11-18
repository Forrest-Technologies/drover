namespace Drover.Presentation.ViewModels.Chat;

/// <summary>What stands in for the workspace until there is a bot to talk to.</summary>
public enum WorkspaceState
{
    /// <summary>There is a crew: the workspace is shown.</summary>
    Ready,

    /// <summary>The space could not be loaded. The reason is said, with a way to try again.</summary>
    Failed,

    /// <summary>The space is being read.</summary>
    Loading,

    /// <summary>No model is connected, so no bot could think.</summary>
    NoModel,

    /// <summary>A model is connected and there is no bot to use it.</summary>
    NoBot,
}
