namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// Which parts of the native search screen are on the page. The screen shows the clear control while a search
/// stands in place of the crew, the empty state when nothing matched, the failure when the search could not be
/// done, and the result links when messages were found.
/// </summary>
/// <param name="ShowsClear">The clear control is on the page.</param>
/// <param name="ShowsFailed">The failure, with its try-again action, is on the page.</param>
/// <param name="ShowsNothing">The empty state, with its heading and clear action, is on the page.</param>
/// <param name="ShowsResults">The result links are on the page, in an arrow-key group named "Messages found".</param>
public readonly record struct ConversationSearchScreen(bool ShowsClear, bool ShowsFailed, bool ShowsNothing, bool ShowsResults)
{
    /// <summary>The parts the screen shows for this search.</summary>
    public static ConversationSearchScreen For(ConversationSearchState state, bool active) =>
        new(
            active,
            state is ConversationSearchState.Failed,
            state is ConversationSearchState.NothingFound,
            state is ConversationSearchState.Found);
}
