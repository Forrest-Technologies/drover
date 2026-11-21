namespace Drover.Native.Droid;

/// <summary>
/// A one-line editor whose text, selection, and composing region are owned by Android. Addresses use it so input
/// methods compose them as Android does; a user name or password uses it so the system's autofill service sees a
/// real field with its hint, which a field drawn by the app cannot give it.
/// </summary>
internal sealed class AndroidTextEditor : UserControl
{
    #region Constructors

    public AndroidTextEditor(Android.Text.InputTypes inputType, string? autofillHint = null)
    {
        var editor = new Android.Widget.EditText(Uno.UI.ContextHelper.Current ?? AndroidApplication.Context);
        editor.SetSingleLine();
        editor.InputType = inputType;
        if (autofillHint is not null && OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            editor.SetAutofillHints(autofillHint);
            editor.ImportantForAutofill = Android.Views.ImportantForAutofill.Yes;
        }

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
        editor.Click += (_, _) => ShowKeyboard();
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

    public void Describe(string label, bool enabled, Color color, int maxLength)
    {
        Editor.ContentDescription = label;
        Editor.SetFilters(maxLength > 0 ? [new Android.Text.InputFilterLengthFilter(maxLength)] : []);
        Editor.Enabled = enabled;
        IsEnabled = enabled;
        Editor.SetTextColor(new Android.Graphics.Color(color.R, color.G, color.B, color.A));
    }

    public void ShowKeyboard()
    {
        var editor = Editor;
        editor.Post(() =>
        {
            if (editor.Enabled && editor.RequestFocus())
            {
                using var keyboard = new AndroidX.Core.View.SoftwareKeyboardControllerCompat(editor);
                keyboard.Show();
            }
        });
    }

    #endregion
}
