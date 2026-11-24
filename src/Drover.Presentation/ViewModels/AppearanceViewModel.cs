namespace Drover.Presentation.ViewModels;

/// <summary>
/// The appearance choices the account menu opens: the theme, the mode, and the accent, each with the one in use
/// marked. A choice applies at once and the choices stay open, so a person can try one after another. Colors always
/// change at once. Shapes, sizes and type need the open screen made again, which would drop what a person typed
/// there and has not saved or sent, so while the screen holds such work they wait until it is left.
/// </summary>
public sealed partial class AppearanceViewModel : ObservableObject, IDisposable
{
    #region Private Fields

    private static readonly (ThemeMode Mode, string Name)[] themeNames =
    [
        (ThemeMode.Dark, "Dark"),
        (ThemeMode.Light, "Light"),
        (ThemeMode.Black, "Black"),
        (ThemeMode.System, "Match this device"),
    ];

    private readonly ThemeModel theme;

    private readonly Navigator navigator;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WaitingNote))]
    private bool shapesWait;

    #endregion

    #region Constructors

    // A traditional constructor, because the choices listen to the theme from the moment they exist.
#pragma warning disable IDE0290
    public AppearanceViewModel(ThemeModel theme, Navigator navigator)
#pragma warning restore IDE0290
    {
        this.theme = theme;
        this.navigator = navigator;
        theme.PropertyChanged += ThemeChanged;
    }

    #endregion

    #region Properties

    /// <summary>True while the choices are showing.</summary>
    public bool Open => theme.AppearanceOpen;

    /// <summary>
    /// Said under the choices while the open screen keeps the shapes it was made with, and nothing otherwise.
    /// </summary>
    public string? WaitingNote => ShapesWait
        ? "Shapes and sizes change when you leave this screen, so your unsaved work is kept."
        : null;

    public IReadOnlyList<ThemeOption> Themes =>
        [.. themeNames.Select(name => new ThemeOption(name.Mode, name.Name, theme.Choice.Mode == name.Mode))];

    /// <summary>The themes the app has, the default first.</summary>
    public IReadOnlyList<LookOption> Looks =>
    [
        .. BuiltInThemes.All.Select(look =>
            new LookOption(look.Id, look.Name, look.Intent, look.Id == theme.Chosen.Id)),
    ];

    /// <summary>
    /// The accents the theme in use offers, its own color first. One that is not offered is not listed, so a theme
    /// with no other accent lists only its own.
    /// </summary>
    public IReadOnlyList<AccentOption> AccentChoices =>
    [
        .. Accents.For(theme.Chosen).Select(accent => new AccentOption(
            accent.Id,
            accent.Name,
            theme.Chosen.WithAccent(accent.Id).Dark.Colors["ember"],
            theme.Chosen.WithAccent(accent.Id).Light.Colors["ember"],
            ReferenceEquals(theme.Chosen.WithAccent(accent.Id), theme.Theme))),
    ];

    #endregion

    #region Public Methods

    public void Dispose() => theme.PropertyChanged -= ThemeChanged;

    [RelayCommand]
    public void ChooseTheme(ThemeMode mode) => theme.Choose(mode);

    [RelayCommand]
    public void ChooseLook(string themeId) => theme.ChooseTheme(themeId);

    [RelayCommand]
    public void ChooseAccent(string accentId) => theme.ChooseAccent(accentId);

    [RelayCommand]
    public void Close() => theme.AppearanceOpen = false;

    /// <summary>
    /// Asked by the shell once a choice has put new shapes and sizes in place: whether to make the open screen again
    /// from them now. Not while the screen holds work that making it again would drop: something a person typed in
    /// it, as the shell sees, or what the screen itself says it holds. It then waits until the screen is left.
    /// </summary>
    public bool MakesScreenAgain(bool somethingTyped)
    {
        ShapesWait = somethingTyped || navigator.HoldsWork?.Invoke() is true;
        return !ShapesWait;
    }

    /// <summary>Told by the shell when a screen has been made, which is always from the values in place.</summary>
    public void ScreenMade() => ShapesWait = false;

    #endregion

    #region Private Methods

    private void ThemeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (change.PropertyName is nameof(ThemeModel.AppearanceOpen))
        {
            OnPropertyChanged(nameof(Open));
            return;
        }

        OnPropertyChanged(nameof(Themes));
        OnPropertyChanged(nameof(Looks));
        OnPropertyChanged(nameof(AccentChoices));
    }

    #endregion
}
