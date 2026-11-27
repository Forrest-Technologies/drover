namespace Drover.Contracts.Cards;

/// <summary>
/// The numbers of the limits table in docs/ux/agent-cards.md, section 1.3, in one place. Lower bounds and the length
/// of one label are fixed. Upper bounds on a count, and the stored size of a card, are defaults: a self-hosted
/// operator may raise them or set no limit, and the server reads the bound in force from its deployment policy.
/// </summary>
public static class CardLimits
{
    #region Counts

    public const int DefaultMaxCardsPerReply = 3;

    public const int MinReplies = 2;

    public const int DefaultMaxReplies = 4;

    public const int MinChoices = 2;

    public const int DefaultMaxChoices = 8;

    public const int MinFields = 1;

    public const int DefaultMaxFields = 6;

    public const int MinChoiceOptions = 2;

    public const int DefaultMaxChoiceOptions = 8;

    public const int MinChecklistItems = 1;

    public const int DefaultMaxChecklistItems = 20;

    public const int MinTableColumns = 1;

    public const int DefaultMaxTableColumns = 8;

    public const int MinTableRows = 1;

    public const int DefaultMaxTableRows = 50;

    public const int MinBoardFigures = 1;

    public const int DefaultMaxBoardFigures = 4;

    public const int MinBoardStates = 0;

    public const int DefaultMaxBoardStates = 6;

    public const int MinBoardRows = 0;

    public const int DefaultMaxBoardRows = 8;

    #endregion

    #region Lengths

    public const int MinTitleCharacters = 1;

    public const int MaxTitleCharacters = 200;

    public const int MinTextCharacters = 0;

    public const int MaxTextCharacters = 500;

    public const int MinReplyCharacters = 1;

    public const int MaxReplyCharacters = 40;

    public const int MinChoiceCharacters = 1;

    public const int MaxChoiceCharacters = 120;

    public const int MinFieldLabelCharacters = 1;

    public const int MaxFieldLabelCharacters = 60;

    public const int MinTextValueCharacters = 1;

    public const int MaxTextValueCharacters = 200;

    public const int MinChecklistItemCharacters = 1;

    public const int MaxChecklistItemCharacters = 120;

    public const int MinColumnNameCharacters = 1;

    public const int MaxColumnNameCharacters = 60;

    public const int MinCellCharacters = 0;

    public const int MaxCellCharacters = 200;

    public const int MinFigureValueCharacters = 1;

    public const int MaxFigureValueCharacters = 16;

    public const int MinFigureLabelCharacters = 1;

    public const int MaxFigureLabelCharacters = 40;

    public const int MinStateLabelCharacters = 1;

    public const int MaxStateLabelCharacters = 40;

    public const int MinRowLabelCharacters = 1;

    public const int MaxRowLabelCharacters = 80;

    public const int MinRowValueCharacters = 0;

    public const int MaxRowValueCharacters = 40;

    public const int MaxLinkCharacters = 2000;

    public const int MaxSomethingElseCharacters = 2000;

    #endregion

    #region Storage

    public const int DefaultMaxCardBytes = 16 * 1024;

    #endregion

    #region Patterns

    public const string FieldNamePattern = "^[a-z][a-z0-9_]{0,31}$";

    #endregion
}
