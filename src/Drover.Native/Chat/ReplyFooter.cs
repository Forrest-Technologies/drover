namespace Drover.Native.Chat;

/// <summary>
/// One quiet line under a finished reply: copy the reply, ask for another, switch between its versions, and, on a
/// reply that is not the newest, a hint of what wrote it and what it used (<see cref="UsageHint"/>). The newest
/// reply's usage stands on the line under the composer instead. What an older reply used is read only when asked for:
/// a conversation with many replies makes no request until a person opens one. On a narrow screen the hint, last on
/// the line, moves to a line of its own before any button does.
/// </summary>
public sealed class ReplyFooter : Grid
{
    #region Private Fields

    private readonly FlowPanel line = new() { Gap = Tokens.Number("DvSpace1") };

    private readonly CopyButton copy = new() { Label = CopyLabel, Ink = "DvText3", VerticalAlignment = VerticalAlignment.Center };

    private readonly IconButton regenerate = new()
    {
        Icon = IconName.Regenerate,
        Label = ChatRows.RegenerateLabel,
        Ink = "DvText3",
        VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed,
    };

    // The versions' switch stands here when the message is a reply that has versions.
    private readonly StackPanel versionsSlot = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly UsageHint usage = new() { VerticalAlignment = VerticalAlignment.Center };

    // The hint stands here on a reply whose usage is not on the line under the composer.
    private readonly StackPanel usageSlot = new() { VerticalAlignment = VerticalAlignment.Center };

    private string copyText = string.Empty;

    private bool usageOffered;

    #endregion

    #region Constructors

    public ReplyFooter()
    {
        copy.Text = () => copyText;
        usage.Closed += () => UsageClosed?.Invoke();

        regenerate.Click += async (_, _) => await Regenerate();

        // The parts stand in the order a footer's parts are named, so the line and its tests agree.
        foreach (var part in Enum.GetValues<FooterPart>())
        {
            line.Children.Add(PartOf(part));
        }

        usageSlot.Children.Add(usage);
        Children.Add(line);
    }

    #endregion

    #region Properties

    /// <summary>What the reply used, and the model that wrote it. Without it the line shows no hint.</summary>
    public ReplyUsageViewModel? Model
    {
        get => usage.Model;
        set => usage.Model = value;
    }

    /// <summary>True while the popover of what the reply used is open, so the reply keeps this line in view.</summary>
    public bool UsageOpen => usage.IsOpen;

    /// <summary>Raised when that popover has closed.</summary>
    public event Action? UsageClosed;

    /// <summary>True when the line holds anything to use, so the reply shows it.</summary>
    public bool HasAny =>
        copy.Visibility is Visibility.Visible
        || regenerate.Visibility is Visibility.Visible
        || (usageOffered && usage.Visibility is Visibility.Visible)
        || versionsSlot.Children.Any(part => part.Visibility is Visibility.Visible);

    /// <summary>Asks for another reply. Without it, the line offers none.</summary>
    public Func<Task>? OnRegenerate { get; set; }

    #endregion

    #region Public Methods

    /// <summary>Puts the versions' switch on this line.</summary>
    public void Hold(UIElement versions)
    {
        if (!versionsSlot.Children.Contains(versions))
        {
            versionsSlot.Children.Add(versions);
        }
    }

    /// <summary>Shows the parts of the line the reply has (<see cref="ChatRows.FooterParts"/>), with its words to copy.</summary>
    public void Show(string replyText, IReadOnlyList<FooterPart> parts)
    {
        copyText = parts.Contains(FooterPart.Copy) ? replyText : string.Empty;
        copy.Visibility = copyText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        regenerate.Visibility = parts.Contains(FooterPart.Regenerate) && OnRegenerate is not null ? Visibility.Visible : Visibility.Collapsed;
        usageOffered = parts.Contains(FooterPart.Usage);
        usageSlot.Visibility = usageOffered ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Puts the focus on the regenerate icon.</summary>
    public void TakeFocus() => regenerate.TakeFocus();

    #endregion

    #region Private Methods

    private FrameworkElement PartOf(FooterPart part) => part switch
    {
        FooterPart.Copy => copy,
        FooterPart.Regenerate => regenerate,
        FooterPart.Versions => versionsSlot,
        _ => usageSlot,
    };

    private async Task Regenerate()
    {
        if (OnRegenerate is { } regenerateReply)
        {
            await regenerateReply();
        }
    }

    #endregion

    #region Fixed Words

    public const string CopyLabel = "Copy reply";

    #endregion
}
