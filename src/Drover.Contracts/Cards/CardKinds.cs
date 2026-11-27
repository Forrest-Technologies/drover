namespace Drover.Contracts.Cards;

/// <summary>The closed set of kind names and state names a card can carry.</summary>
public static class CardKinds
{
    #region Kinds

    public const string Replies = "replies";

    public const string Choices = "choices";

    public const string Form = "form";

    public const string Checklist = "checklist";

    public const string Table = "table";

    public const string Board = "board";

    #endregion

    #region Item States

    public const string ItemToDo = "to_do";

    public const string ItemDoing = "doing";

    public const string ItemDone = "done";

    public const string ItemFailed = "failed";

    #endregion

    #region Board States

    public const string BoardOk = "ok";

    public const string BoardAttention = "attention";

    public const string BoardFailed = "failed";

    public const string BoardUnknown = "unknown";

    #endregion
}
