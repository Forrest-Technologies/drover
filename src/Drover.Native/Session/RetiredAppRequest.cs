namespace Drover.Native.Session;

/// <summary>
/// Asks, at each start while the retired Drover Preview app is still installed, for it to be removed. Its icon looks
/// like this app's, and what a person changes in it never reaches this one. The shell holds it over every screen.
/// </summary>
public sealed class RetiredAppRequest : Grid
{
    #region Private Fields

    private readonly RetiredAppViewModel model;

    private readonly Dialog dialog = new() { Title = RetiredAppViewModel.Title };

    #endregion

    #region Constructors

    public RetiredAppRequest(RetiredAppViewModel model)
    {
        this.model = model;
        var later = new Button { Content = RetiredAppViewModel.LaterLabel };
        var remove = new Button { Content = RetiredAppViewModel.RemoveLabel, Kind = ButtonKind.Primary };
        later.Click += (_, _) => model.Later();
        remove.Click += (_, _) => model.Remove();
        dialog.Body = Styles.Text(RetiredAppViewModel.Sentence, "DvTextParagraph");
        dialog.Actions = PageParts.Row(later, remove);

        // Escape, Back or the Close button close it as Not now does.
        dialog.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) => Closed());
        model.PropertyChanged += (_, _) => dialog.Open = model.Open;
        Children.Add(dialog);
        Loaded += (_, _) => model.Check();
    }

    #endregion

    #region Private Methods

    private void Closed()
    {
        if (!dialog.Open && model.Open)
        {
            model.Later();
        }
    }

    #endregion
}
