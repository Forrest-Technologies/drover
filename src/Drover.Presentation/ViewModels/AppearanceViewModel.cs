namespace Drover.Presentation.ViewModels;

/// <summary>
/// The appearance choices the account menu opens: the theme, the mode, and the accent, each with the one in use
/// marked. A choice applies at once and the choices stay open, so a person can try one after another.
/// </summary>
public sealed partial class AppearanceViewModel : ObservableObject, IDisposable
{
    #region Private Fields

    private static readonly (ThemeMode Mode, string Name)[] themeNames =
    [
        (ThemeMode.Dark, "Dark theme"),
        (ThemeMode.Light, "Light theme"),
        (ThemeMode.Black, "Black theme"),
        (ThemeMode.System, "Match this device"),
    ];

    private readonly ThemeModel theme;

    #endregion

    #region Constructors

    // A traditional constructor, because the choices listen to the theme from the moment they exist.
#pragma warning disable IDE0290
    public AppearanceViewModel(ThemeModel theme)
#pragma warning restore IDE0290
    {
        this.theme = theme;
        theme.PropertyChanged += ThemeChanged;
    }

    #endregion

    #region Properties

    /// <summary>True while the choices are showing.</summary>
    public bool Open => theme.AppearanceOpen;

    public IReadOnlyList<ThemeOption> Themes =>
        [.. themeNames.Select(name => new ThemeOption(name.Mode, name.Name, theme.Choice.Mode == name.Mode))];

    /// <summary>The themes the app has, the default first.</summary>
    public IReadOnlyList<LookOption> Looks =>
        [.. BuiltInThemes.All.Select(look => new LookOption(look.Id, look.Name, look.Intent, look.Id == theme.Chosen.Id))];

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
