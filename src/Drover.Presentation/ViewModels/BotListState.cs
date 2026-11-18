namespace Drover.Presentation.ViewModels;

/// <summary>What the list of bots shows: one of these at a time.</summary>
public enum BotListState
{
    Loading,
    Failed,
    NoModel,
    NoBot,
    NoBotForMember,
    Listed,
}
