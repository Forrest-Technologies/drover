namespace Drover.Native.Pages;

/// <summary>
/// Where a person chooses which bot's conversation gets what another app shared, as a draft. Nothing is sent until
/// they send it.
/// </summary>
public sealed partial class ShareToPage : Screen
{
    #region Constructors

    public ShareToPage()
    {
        InitializeComponent();
        Column.Padding = Tokens.Space("DvSpace4", "DvSpace5");
    }

    #endregion

    #region Properties

    public override string Title => ShareToViewModel.Title;

    public ShareToViewModel Model { get; } = Make<ShareToViewModel>();

    #endregion

    #region Public Methods

    /// <summary>A file size a person can compare: "840 bytes", "12 KB", "3.4 MB".</summary>
    public static string SizeOf(long bytes) => Words.Size(bytes);

    #endregion

    #region Private Methods

    protected override async void Opened(RouteMatch<Type> route) => await Model.Start();

    #endregion
}
