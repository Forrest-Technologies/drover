namespace Drover.Presentation.Code;

/// <summary>
/// The full screen view of a block of code or output: what it shows, whether its lines wrap, and whether it is open.
/// It opens on the block's own wrap choice, and changing it here leaves the block as it was. While it is open, a
/// device's Back closes it before anything else.
/// </summary>
public sealed partial class CodeViewerModel(Navigator? navigator) : ObservableObject
{
    #region Private Fields

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private CodeContent? content;

    [ObservableProperty]
    private bool wraps;

    [ObservableProperty]
    private bool isOpen;

    #endregion

    #region Properties

    /// <summary>The view's name as a dialog: "Full screen: Table, 12 rows".</summary>
    public string Title => Content is { } shown ? FullScreenTitle(shown.Label) : string.Empty;

    /// <summary>Raised once the view has closed, by its close button, Escape, or Back, so the focus can go home.</summary>
    public event Action? Closed;

    #endregion

    #region Public Methods

    /// <summary>
    /// What the view draws of a block: all of it, and a table as a grid only while all of it keeps to a grid's bounds
    /// (<see cref="CodeLines.TableLayoutOf"/>). Past them, the table is drawn as its text, which costs no more than its
    /// characters, so opening a table of a great many cells cannot stall the screen.
    /// </summary>
    public static CodeContent Viewed(CodeContent content) =>
        content is { Kind: CodeKind.Table, Table: { } table } && CodeLines.TableLayoutOf(table, all: true) is TableLayout.Text
            ? content with { Kind = CodeKind.Plain, Family = CodeFamily.None }
            : content;

    /// <summary>The name of the full screen view of a block with this label.</summary>
    public static string FullScreenTitle(string label) => $"Full screen: {label}";

    /// <summary>Opens the view on a block's content, starting from the block's wrap choice.</summary>
    public void Show(CodeContent shown, bool blockWraps)
    {
        Content = shown;
        Wraps = blockWraps;

        if (!IsOpen)
        {
            IsOpen = true;
            navigator?.AddCloser(CloseFromBack);
        }
    }

    [RelayCommand]
    public void ToggleWrap() => Wraps = !Wraps;

    [RelayCommand]
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        navigator?.RemoveCloser(CloseFromBack);
        Closed?.Invoke();
        IsOpen = false;
    }

    #endregion

    #region Private Methods

    // Back has already taken this closer off the navigator.
    private void CloseFromBack()
    {
        IsOpen = false;
        Closed?.Invoke();
    }

    #endregion

    #region Fixed Words

    public const string OpenLabel = "Open full screen";

    public const string CloseLabel = "Close full screen";

    #endregion
}
