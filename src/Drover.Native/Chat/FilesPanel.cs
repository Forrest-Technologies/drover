namespace Drover.Native.Chat;

/// <summary>
/// STAND-IN: replaced at merge by the real control.
/// It has the surface of the web client's <c>FilesPanel</c> and shows only its own name, so the screen that hosts it builds.
/// </summary>
public sealed class FilesPanel : Grid
{
    #region Constructors

    public FilesPanel()
    {
        Children.Add(Styles.Text(nameof(FilesPanel), "DvTextCaption"));
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty StateProperty = Dp.Register<FilesPanel, FilesPanelState>(nameof(State), default(FilesPanelState));

    public static readonly DependencyProperty BotNameProperty = Dp.Register<FilesPanel, string>(nameof(BotName), string.Empty);

    public static readonly DependencyProperty FilesProperty = Dp.Register<FilesPanel, IReadOnlyList<SharedFile>>(nameof(Files), []);

    public static readonly DependencyProperty ProblemProperty = Dp.Register<FilesPanel, string?>(nameof(Problem), null);

    public static readonly DependencyProperty ReadWholeProperty = Dp.Register<FilesPanel, Func<SharedFile, Task<FileText>>?>(nameof(ReadWhole), null);

    public FilesPanelState State
    {
        get => (FilesPanelState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string BotName
    {
        get => (string)GetValue(BotNameProperty);
        set => SetValue(BotNameProperty, value);
    }

    public IReadOnlyList<SharedFile> Files
    {
        get => (IReadOnlyList<SharedFile>)GetValue(FilesProperty);
        set => SetValue(FilesProperty, value);
    }

    public string? Problem
    {
        get => (string?)GetValue(ProblemProperty);
        set => SetValue(ProblemProperty, value);
    }

    public Func<SharedFile, Task<FileText>>? ReadWhole
    {
        get => (Func<SharedFile, Task<FileText>>?)GetValue(ReadWholeProperty);
        set => SetValue(ReadWholeProperty, value);
    }

#pragma warning disable CS0067 // The stand-in never raises it; the real control does.
    public event EventHandler? OnRetry;
#pragma warning restore CS0067

    #endregion
}
