namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// Work one bot handed to a bot beneath it, as a card in the parent's conversation. Closed: who asked whom for what,
/// and the state in words. Open: the whole task, the child's own activity, and the child's answer. The card's edge is
/// violet while the work is out, then the result's color.
/// </summary>
public sealed class DelegationCardViewModel : ObservableObject
{
    #region Private Fields

    private bool open;

    private DelegationPhase? seen;

    #endregion

    #region Properties

    public DelegationItem Item { get; set; } = null!;

    /// <summary>True when the card was given the child's activity to show.</summary>
    public bool HasWork { get; set; }

    public bool Open
    {
        get => open;
        set
        {
            if (open != value)
            {
                open = value;
                Changed();
            }
        }
    }

    public string Title => DelegationWords.Sentence(Item);

    public string State => DelegationWords.State(Item.Phase);

    public BadgeKind StateKind => DelegationWords.Kind(Item.Phase);

    public AvatarState Head => DelegationWords.Head(Item.Phase);

    public int Hue => BotIdentity.Hue(Item.ChildId);

    /// <summary>The frame's edge as a color token: violet while the work is out, then the result's color. The usual edge when null.</summary>
    public string? Edge => Item.Phase switch
    {
        _ when Item.IsOut => "DvViolet",
        DelegationPhase.Done => "DvOk",
        DelegationPhase.Failed => "DvDanger",
        _ => null,
    };

    public bool IsRefused => Item.Phase is DelegationPhase.Refused;

    public string RefusedLine => DelegationWords.Refused(Item.Refusal, Item.ChildName, Item.RefusalLimit);

    /// <summary>The bot's Limits settings, when a handoff limit refused this call and the person can edit the bot.</summary>
    public string? LimitsHref => IsRefused && Item.Refusal is DelegationRefusal.TooManyHandoffs ? Item.LimitsHref : null;

    public string? LimitsLinkText => LimitsHref is not null ? AttentionWords.OpenLimitsLabel : null;

    public string TaskTitle => $"What {Item.ChildName} was asked";

    /// <summary>Past the deepest level shown, the card says where the work is instead of nesting it.</summary>
    public string? ElsewhereLine =>
        !IsRefused && Item.Depth >= DelegationWords.DeepestShown && Item.ChildHref is not null ? $"{Item.ChildName}'s work is shown on its own side." : null;

    public bool ShowsWork => !IsRefused && Item.Depth < DelegationWords.DeepestShown && HasWork;

    public string WorkTitle => $"What {Item.ChildName} did";

    public string? Answer => !IsRefused && Item.Answer is { Length: > 0 } answer ? answer : null;

    public string AnswerTitle => $"What {Item.ChildName} answered";

    /// <summary>
    /// True when the answer is a raw tool request, whole or cut off. The card then shows the message list's card in
    /// place of the text: the same words, and Try again only where that message would offer it.
    /// </summary>
    public bool AnswerUnusable => Answer is { } answer && UnusableToolRequests.IsUnusableRequest(answer);

    public string? LinkHref => IsRefused ? null : Item.ChildHref;

    public string LinkText => $"Open {Item.ChildName}'s side of this";

    /// <summary>The titles of the parts an open card holds, in order.</summary>
    public IReadOnlyList<string> PartTitles => IsRefused
        ? []
        :
        [
            TaskTitle,
            .. ShowsWork ? [WorkTitle] : Array.Empty<string>(),
            .. Answer is not null ? [AnswerTitle] : Array.Empty<string>(),
        ];

    #endregion

    #region Public Methods

    /// <summary>Runs once the card's values are set, and again each time one changes.</summary>
    public void ParametersSet()
    {
        // A child that starts waiting for a person opens the card, so the answer buttons are in view. Only on
        // the change: a person who closes it again is not overruled.
        if (Item.Phase is DelegationPhase.WaitingForYou && seen is not DelegationPhase.WaitingForYou)
        {
            open = true;
        }

        seen = Item.Phase;
    }

    #endregion

    #region Private Methods

    private void Changed() => OnPropertyChanged(string.Empty);

    #endregion
}
