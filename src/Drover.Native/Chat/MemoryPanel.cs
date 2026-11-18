namespace Drover.Native.Chat;

/// <summary>
/// STAND-IN: replaced at merge by the real control.
/// It has the surface of the web client's <c>MemoryPanel</c> and shows only its own name, so the screen that hosts it builds.
/// </summary>
public sealed class MemoryPanel : Grid
{
    #region Constructors

    public MemoryPanel()
    {
        Children.Add(Styles.Text(nameof(MemoryPanel), "DvTextCaption"));
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty StateProperty = Dp.Register<MemoryPanel, MemoryPanelState>(nameof(State), default(MemoryPanelState));

    public static readonly DependencyProperty BotNameProperty = Dp.Register<MemoryPanel, string>(nameof(BotName), string.Empty);

    public static readonly DependencyProperty NotesProperty = Dp.Register<MemoryPanel, IReadOnlyList<MemoryNote>>(nameof(Notes), []);

    public static readonly DependencyProperty NearlyFullProperty = Dp.Register<MemoryPanel, bool>(nameof(NearlyFull), false);

    public static readonly DependencyProperty ProblemProperty = Dp.Register<MemoryPanel, string?>(nameof(Problem), null);

    public static readonly DependencyProperty EditorHrefProperty = Dp.Register<MemoryPanel, string?>(nameof(EditorHref), null);

    public static readonly DependencyProperty OnAddProperty = Dp.Register<MemoryPanel, Func<string, Task<string?>>?>(nameof(OnAdd), null);

    public static readonly DependencyProperty OnChangeProperty = Dp.Register<MemoryPanel, Func<Guid, string, Task<string?>>?>(nameof(OnChange), null);

    public MemoryPanelState State
    {
        get => (MemoryPanelState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string BotName
    {
        get => (string)GetValue(BotNameProperty);
        set => SetValue(BotNameProperty, value);
    }

    public IReadOnlyList<MemoryNote> Notes
    {
        get => (IReadOnlyList<MemoryNote>)GetValue(NotesProperty);
        set => SetValue(NotesProperty, value);
    }

    public bool NearlyFull
    {
        get => (bool)GetValue(NearlyFullProperty);
        set => SetValue(NearlyFullProperty, value);
    }

    public string? Problem
    {
        get => (string?)GetValue(ProblemProperty);
        set => SetValue(ProblemProperty, value);
    }

    public string? EditorHref
    {
        get => (string?)GetValue(EditorHrefProperty);
        set => SetValue(EditorHrefProperty, value);
    }

    public Func<string, Task<string?>>? OnAdd
    {
        get => (Func<string, Task<string?>>?)GetValue(OnAddProperty);
        set => SetValue(OnAddProperty, value);
    }

    public Func<Guid, string, Task<string?>>? OnChange
    {
        get => (Func<Guid, string, Task<string?>>?)GetValue(OnChangeProperty);
        set => SetValue(OnChangeProperty, value);
    }

#pragma warning disable CS0067 // The stand-in never raises it; the real control does.
    public event EventHandler<Guid>? OnRemove;
#pragma warning restore CS0067

#pragma warning disable CS0067 // The stand-in never raises it; the real control does.
    public event EventHandler? OnRetry;
#pragma warning restore CS0067

    #endregion
}
