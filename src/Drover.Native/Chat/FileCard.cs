namespace Drover.Native.Chat;

/// <summary>
/// STAND-IN: replaced at merge by the real control.
/// It has the surface of the web client's <c>FileCard</c> and shows only its own name, so the screen that hosts it builds.
/// </summary>
public sealed class FileCard : Grid
{
    #region Constructors

    public FileCard()
    {
        Children.Add(Styles.Text(nameof(FileCard), "DvTextCaption"));
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty FileProperty = Dp.Register<FileCard, SharedFile?>(nameof(File), null);

    public static readonly DependencyProperty ReadWholeProperty = Dp.Register<FileCard, Func<SharedFile, Task<FileText>>?>(nameof(ReadWhole), null);

    public SharedFile? File
    {
        get => (SharedFile?)GetValue(FileProperty);
        set => SetValue(FileProperty, value);
    }

    public Func<SharedFile, Task<FileText>>? ReadWhole
    {
        get => (Func<SharedFile, Task<FileText>>?)GetValue(ReadWholeProperty);
        set => SetValue(ReadWholeProperty, value);
    }

    #endregion
}
