namespace Drover.Native.Droid;

/// <summary>An address editor whose text, selection, and composing region are owned by Android.</summary>
internal sealed class AndroidAddressEditor : UserControl
{
    #region Constructors

    public AndroidAddressEditor()
    {
        var editor = new Android.Widget.EditText(Uno.UI.ContextHelper.Current ?? AndroidApplication.Context);
        editor.SetSingleLine();
        editor.InputType = Android.Text.InputTypes.ClassText
            | Android.Text.InputTypes.TextVariationUri
            | Android.Text.InputTypes.TextFlagNoSuggestions;
        editor.ImeOptions = Android.Views.InputMethods.ImeAction.Done;
        editor.SetBackgroundColor(Android.Graphics.Color.Transparent);
        editor.SetTextSize(Android.Util.ComplexUnitType.Sp, (float)Tokens.Number("DvText14"));
        editor.Typeface = Android.Graphics.Typeface.CreateFromAsset(editor.Context?.Assets, "Assets/Fonts/Inter_Regular.ttf");
        editor.SetIncludeFontPadding(false);
        editor.Gravity = Android.Views.GravityFlags.Start | Android.Views.GravityFlags.CenterVertical;
        var density = editor.Resources?.DisplayMetrics?.Density ?? 1f;
        var inset = (int)Math.Round(Tokens.Number("DvSpace3") * density);
        editor.SetPadding(inset, 0, inset, 0);
        editor.TextChanged += (_, _) => TextChanged?.Invoke(this, EventArgs.Empty);
        editor.FocusChange += (_, args) =>
        {
            if (args.HasFocus && FocusState is FocusState.Unfocused)
            {
                Focus(FocusState.Programmatic);
            }
        };
        editor.EditorAction += (_, args) =>
        {
            if (args.ActionId is Android.Views.InputMethods.ImeAction.Done && args.Event is null)
            {
                args.Handled = true;
                Submitted?.Invoke(this, EventArgs.Empty);
            }
        };

        IsTabStop = true;
        Content = new ContentPresenter { Content = editor };
        GotFocus += (_, _) => editor.RequestFocus();
        LostFocus += (_, _) => editor.ClearFocus();
    }

    #endregion

    #region Properties

    private Android.Widget.EditText Editor => (Android.Widget.EditText)((ContentPresenter)Content).Content;

    public string Text
    {
        get => Editor.Text ?? string.Empty;
        set => Editor.Text = value;
    }

    public event EventHandler? TextChanged;

    public event EventHandler? Submitted;

    #endregion

    #region Public Methods

    public void Describe(string label, bool enabled, Color color)
    {
        Editor.ContentDescription = label;
        Editor.Enabled = enabled;
        IsEnabled = enabled;
        Editor.SetTextColor(new Android.Graphics.Color(color.R, color.G, color.B, color.A));
    }

    #endregion
}
