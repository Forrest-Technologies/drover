namespace Drover.Native.Chat;

/// <summary>
/// One quiet line of icons under a finished reply: copy the reply, the usage of the reply, ask for another, and switch
/// between its versions. "Usage for this reply" opens a popover of the model that wrote it and what it used
/// (<see cref="UsagePopover"/>); no model, tokens or cost are written under the reply. What a reply used is read only
/// when that is pressed: a conversation with many replies makes no request until a person opens one.
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

    private readonly IconButton usage = new()
    {
        Icon = IconName.Info,
        Label = ReplyUsageViewModel.Label,
        Ink = "DvText3",
        VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed,
    };

    private readonly UsagePopover popover = new();

    private string copyText = string.Empty;

    #endregion

    #region Constructors

    public ReplyFooter()
    {
        copy.Text = () => copyText;
        popover.Closed += () => UsageClosed?.Invoke();
        usage.Click += async (_, _) => await popover.Open(usage);

        regenerate.Click += async (_, _) => await Regenerate();

        // The parts stand in the order a footer's parts are named, so the line and its tests agree.
        foreach (var part in Enum.GetValues<FooterPart>())
        {
            line.Children.Add(PartOf(part));
        }

        Children.Add(line);
    }

    #endregion

    #region Properties

    /// <summary>What the reply used, and the model that wrote it. Without it the line offers no usage.</summary>
    public ReplyUsageViewModel? Model
    {
        get => popover.Model;
        set => popover.Model = value;
    }

    /// <summary>True while the popover of what the reply used is open, so the reply keeps this line in view.</summary>
    public bool UsageOpen => popover.IsOpen;

    /// <summary>Raised when that popover has closed.</summary>
    public event Action? UsageClosed;

    /// <summary>True when the line holds anything to use, so the reply shows it.</summary>
    public bool HasAny =>
        copy.Visibility is Visibility.Visible
        || regenerate.Visibility is Visibility.Visible
        || usage.Visibility is Visibility.Visible
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
        usage.Visibility = parts.Contains(FooterPart.Usage) && Model is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Puts the focus on the regenerate icon.</summary>
    public void TakeFocus() => regenerate.TakeFocus();

    #endregion

    #region Private Methods

    private FrameworkElement PartOf(FooterPart part) => part switch
    {
        FooterPart.Copy => copy,
        FooterPart.Regenerate => regenerate,
        FooterPart.Usage => usage,
        _ => versionsSlot,
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
