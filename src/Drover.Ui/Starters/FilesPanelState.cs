namespace Drover.Ui.Starters;

/// <summary>What the files panel has to show.</summary>
public enum FilesPanelState
{
    Loading,
    Failed,
    Ready,
}

/// <summary>Whose files the panel lists.</summary>
public enum FilesFrom
{
    Everyone,
    Bot,
    You,
}
