namespace Drover.Native.Chat;

/// <summary>
/// The word beside a bot's name in the crew, or beside a side chat's title. Nothing is shown when there is
/// nothing to say, so a quiet crew stays quiet.
/// </summary>
public sealed class AttentionNote : Grid
{
    #region Private Fields

    private readonly Badge badge = new();

    #endregion

    #region Constructors

    public AttentionNote()
    {
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(badge);
        Show();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty AttentionProperty =
        Dp.Register<AttentionNote, Attention>(nameof(Attention), Attention.None, note => note.Show());

    public Attention Attention
    {
        get => (Attention)GetValue(AttentionProperty);
        set => SetValue(AttentionProperty, value);
    }

    #endregion

    #region Private Methods

    private void Show()
    {
        var word = AttentionWords.Word(Attention);
        Visibility = word is null ? Visibility.Collapsed : Visibility.Visible;
        badge.Text = word ?? string.Empty;
        badge.Kind = AttentionWords.Kind(Attention);
    }

    #endregion
}
