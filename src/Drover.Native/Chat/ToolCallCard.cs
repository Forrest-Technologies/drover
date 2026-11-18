namespace Drover.Native.Chat;

/// <summary>
/// STAND-IN: replaced at merge by the real control.
/// It has the surface of the web client's <c>ToolCallCard</c> and shows only its own name, so the screen that hosts it builds.
/// </summary>
public sealed class ToolCallCard : Grid
{
    #region Constructors

    public ToolCallCard()
    {
        Children.Add(Styles.Text(nameof(ToolCallCard), "DvTextCaption"));
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty CardProperty = Dp.Register<ToolCallCard, ToolCard?>(nameof(Card), null);

    public static readonly DependencyProperty BotNameProperty = Dp.Register<ToolCallCard, string>(nameof(BotName), string.Empty);

    public static readonly DependencyProperty DecideProperty = Dp.Register<ToolCallCard, Func<Guid, bool, Task<string?>>?>(nameof(Decide), null);

    public static readonly DependencyProperty ForgetProperty = Dp.Register<ToolCallCard, Func<Guid, string, Task<ForgetOutcome>>?>(nameof(Forget), null);

    public static readonly DependencyProperty ForgottenProperty = Dp.Register<ToolCallCard, bool>(nameof(Forgotten), false);

    public static readonly DependencyProperty MemoryHrefProperty = Dp.Register<ToolCallCard, string?>(nameof(MemoryHref), null);

    public ToolCard? Card
    {
        get => (ToolCard?)GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    public string BotName
    {
        get => (string)GetValue(BotNameProperty);
        set => SetValue(BotNameProperty, value);
    }

    public Func<Guid, bool, Task<string?>>? Decide
    {
        get => (Func<Guid, bool, Task<string?>>?)GetValue(DecideProperty);
        set => SetValue(DecideProperty, value);
    }

    public Func<Guid, string, Task<ForgetOutcome>>? Forget
    {
        get => (Func<Guid, string, Task<ForgetOutcome>>?)GetValue(ForgetProperty);
        set => SetValue(ForgetProperty, value);
    }

    public bool Forgotten
    {
        get => (bool)GetValue(ForgottenProperty);
        set => SetValue(ForgottenProperty, value);
    }

    public string? MemoryHref
    {
        get => (string?)GetValue(MemoryHrefProperty);
        set => SetValue(MemoryHrefProperty, value);
    }

    #endregion
}
