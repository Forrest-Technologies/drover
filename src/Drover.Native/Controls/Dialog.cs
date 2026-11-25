namespace Drover.Native.Controls;

/// <summary>
/// A modal dialog: a title, a body, and actions, over the flat dim. While it is open the rest of the app cannot be
/// reached, Tab stays inside it, and Escape closes it. Closing puts the focus back where it was.
/// </summary>
[Microsoft.UI.Xaml.Markup.ContentProperty(Name = nameof(Body))]
public class Dialog : Grid
{
    #region Private Fields

    private readonly DialogSurface surface;

    #endregion

    #region Constructors

    public Dialog()
        : this(atEdge: false)
    {
    }

    private protected Dialog(bool atEdge)
    {
        surface = new(this, atEdge);
        surface.Closed += () => Open = false;
        Loaded += (_, _) => Show();
        Unloaded += (_, _) => surface.Hide();
        ActualThemeChanged += (_, _) => Show();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty OpenProperty = Dp.Register<Dialog, bool>(nameof(Open), false, dialog => dialog.Show());

    public static readonly DependencyProperty TitleProperty = Dp.Register<Dialog, string>(nameof(Title), string.Empty, dialog => dialog.Show());

    public static readonly DependencyProperty BodyProperty = Dp.Register<Dialog, object?>(nameof(Body), null, dialog => dialog.Show());

    public static readonly DependencyProperty ActionsProperty = Dp.Register<Dialog, object?>(nameof(Actions), null, dialog => dialog.Show());

    /// <summary>Whether the dialog is showing. It can be bound both ways: Escape and Close turn it off.</summary>
    public bool Open
    {
        get => (bool)GetValue(OpenProperty);
        set => SetValue(OpenProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>What the dialog holds. It is in the screen only while the dialog is open.</summary>
    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    /// <summary>
    /// For a dialog made in the place of one already showing, as after a change of theme: it opens with no fade.
    /// </summary>
    public bool OpensAtOnce
    {
        get => surface.AtOnce;
        init => surface.AtOnce = value;
    }

    /// <summary>How the person closed the dialog: with the keyboard, or with a pointer or a touch.</summary>
    public FocusState ClosedWith => surface.ClosedWith;

    /// <summary>The buttons at the foot, the one that goes ahead last.</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    #endregion

    #region Internal Methods

    /// <summary>Closes the way Escape does, for a screen check that cannot inject a key.</summary>
    internal void CloseFromKeyboard() => surface.CloseFromKeyboard();

    #endregion

    #region Private Methods

    private void Show()
    {
        if (Open && IsLoaded)
        {
            surface.Show(Title, Body, Actions);
        }
        else
        {
            surface.Hide();
        }
    }

    #endregion
}
