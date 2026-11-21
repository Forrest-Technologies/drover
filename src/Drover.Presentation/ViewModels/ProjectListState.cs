namespace Drover.Presentation.ViewModels;

/// <summary>What the list of projects shows: one of these at a time.</summary>
public enum ProjectListState
{
    Loading,
    Failed,
    Empty,
    Listed,
}
