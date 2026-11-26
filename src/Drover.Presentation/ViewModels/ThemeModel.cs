namespace Drover.Presentation.ViewModels;

/// <summary>A theme the space offers from one of its extensions, checked on this device, and the extension it came from.</summary>
public sealed record OfferedTheme(ThemeFile Theme, string Source);

/// <summary>
/// The theme choice: which theme, in which mode, with which accent. The app is the default theme, dark, until a
/// person picks otherwise, and the choice is kept on the device under the name the web app always used. A value
/// written before a person could choose a theme reads as the default theme in the mode it names. Besides the themes
/// the app has, a space's extensions can offer more: each is checked here, with the checker every theme passes,
/// before it can be chosen, and the one in use is kept on the device so the app starts in it.
/// </summary>
public sealed partial class ThemeModel(IDeviceSettings settings, ILogger<ThemeModel> logger) : ObservableObject
{
    #region Private Fields

    /// <summary>The name of the stored choice. In a browser it is the same local storage entry the web app wrote.</summary>
    public const string SettingName = "drover.theme";

    /// <summary>The name under which the file of an offered theme in use is kept, so the next start can draw it before the server answers.</summary>
    public const string KeptThemeName = "drover.theme.file";

    private Dictionary<string, string> offeredFiles = [];

    private ThemeFile? kept;

    private string? keptFile;

    [ObservableProperty]
    private ThemeChoice choice = ThemeChoice.Default;

    /// <summary>
    /// True while the appearance choices are showing. It is kept here, with the choice, because a choice can make the
    /// open screen again and the choices stay open over it.
    /// </summary>
    [ObservableProperty]
    private bool appearanceOpen;

    #endregion

    #region Properties

    /// <summary>
    /// The theme the choice names, with the accent it names. The default theme when the app has no theme by that
    /// name, as a device that never chose does, and the theme's own color when it does not offer that accent.
    /// </summary>
    public ThemeFile Theme => Chosen.WithAccent(Choice.Accent);

    /// <summary>The theme the choice names as its file holds it, in its own color.</summary>
    public ThemeFile Chosen => Find(Choice.ThemeId) ?? BuiltInThemes.Default;

    /// <summary>The themes the space's extensions offer that passed every check on this device.</summary>
    public IReadOnlyList<OfferedTheme> Offered { get; private set; } = [];

    /// <summary>True when the choice is the black mode, which the dark mode of the window is then drawn in.</summary>
    public bool IsBlack => Choice.Mode is ThemeMode.Black;

    #endregion

    #region Public Methods

    public void Load()
    {
        kept = ReadKept();
        Choice = ThemeChoice.Parse(Stored(), logger);
    }

    /// <summary>
    /// Takes the themes the space offers, as the server lists them. A theme is offered only when it passes every
    /// theme check here and does not take the id of one of the app's own: the server checked it too, and the device
    /// does not take that on trust. Once the space has answered, a theme it no longer offers is no longer drawn, so
    /// the app goes back to the default look until the person picks another.
    /// </summary>
    public void Offer(IReadOnlyList<SpaceTheme> themes)
    {
        List<OfferedTheme> passed = [];
        Dictionary<string, string> files = [];

        foreach (var theme in themes)
        {
            // A file this device already read is the same theme: the one object is kept, so a look in use is not put in place again.
            if ((Known(theme.Id, theme.File) ?? ReadChecked(theme.File, theme.Id)) is { } read && files.TryAdd(read.Id, theme.File))
            {
                passed.Add(new(read, theme.Source));
            }
        }

        (Offered, offeredFiles, kept, keptFile) = (passed, files, null, null);
        Keep();
        OnPropertyChanged(nameof(Offered));
        OnPropertyChanged(nameof(Chosen));
        OnPropertyChanged(nameof(Theme));
    }

    /// <summary>
    /// Applies an account choice and keeps it on this device without deciding whether to send it again.
    /// </summary>
    public void ApplyFromServer(AppearanceSetting setting)
    {
        var chosen = Find(setting.ThemeId) ?? BuiltInThemes.Default;
        var mode = setting.Mode switch
        {
            "system" => ThemeMode.System,
            "light" => ThemeMode.Light,
            "black" => ThemeMode.Black,
            _ => ThemeMode.Dark,
        };
        var accent = Accents.For(chosen).Any(candidate => candidate.Id == setting.Accent)
            ? setting.Accent
            : Accents.ThemeId;

        Choose(new ThemeChoice(chosen.Id, mode, accent));
    }

    /// <summary>Takes another mode of the theme in use.</summary>
    public void Choose(ThemeMode mode) => Choose(Choice with { Mode = mode });

    /// <summary>Takes another accent for the theme in use.</summary>
    public void ChooseAccent(string accentId) => Choose(Choice with { Accent = accentId });

    /// <summary>Takes another theme, in the mode in use and in the theme's own color.</summary>
    public void ChooseTheme(string themeId) => Choose(Choice with { ThemeId = themeId, Accent = Accents.ThemeId });

    public void Choose(ThemeChoice chosen)
    {
        Choice = chosen;

        try
        {
            settings.Write(SettingName, chosen.ToStored());
        }
        catch (InvalidOperationException exception)
        {
            // Storage can be turned off. The choice still holds for this visit.
            logger.LogWarning(exception, "The theme choice could not be kept on this device");
        }

        Keep();
    }

    #endregion

    #region Private Methods

    /// <summary>The theme this device already holds for the same id and the same text, or null.</summary>
    private ThemeFile? Known(string id, string file) =>
        offeredFiles.TryGetValue(id, out var offered) && offered == file ? Offered.First(held => held.Theme.Id == id).Theme
        : kept?.Id == id && keptFile == file ? kept
        : null;

    private ThemeFile? Find(string id) =>
        BuiltInThemes.Get(id)
        ?? Offered.FirstOrDefault(offered => offered.Theme.Id == id)?.Theme
        ?? (kept?.Id == id ? kept : null);

    /// <summary>The theme the text holds, or null when it is not a theme file that passes every check, or it takes one of the app's own ids.</summary>
    private ThemeFile? ReadChecked(string file, string? id)
    {
        if (id is null || ThemeIds.Reserved.Contains(id, StringComparer.Ordinal) || ThemeGates.CheckFile(file, id) is not [])
        {
            if (id is not null)
            {
                logger.LogWarning("An offered theme did not pass the theme checks and is not offered");
            }

            return null;
        }

        return ThemeFileReader.Read(file, id);
    }

    /// <summary>Keeps the file of the offered theme in use on the device, and nothing when the theme in use is the app's own.</summary>
    private void Keep()
    {
        try
        {
            if (offeredFiles.TryGetValue(Choice.ThemeId, out var file))
            {
                settings.Write(KeptThemeName, file);
            }
            else if (kept is null)
            {
                settings.Remove(KeptThemeName);
            }
        }
        catch (InvalidOperationException exception)
        {
            // Storage can be turned off. The theme still holds for this visit.
            logger.LogWarning(exception, "The theme in use could not be kept on this device");
        }
    }

    private ThemeFile? ReadKept()
    {
        try
        {
            keptFile = settings.Read(KeptThemeName);
            return keptFile is { Length: > 0 } file ? ReadChecked(file, IdOf(file)) : null;
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "The theme in use could not be read from this device");
            return null;
        }
    }

    private string? IdOf(string file)
    {
        try
        {
            using var document = JsonDocument.Parse(file);
            return document.RootElement.ValueKind is JsonValueKind.Object
                && document.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind is JsonValueKind.String
                    ? id.GetString()
                    : null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "The theme kept on this device is not a theme file");
            return null;
        }
    }

    private string? Stored()
    {
        try
        {
            return settings.Read(SettingName);
        }
        catch (InvalidOperationException exception)
        {
            // Storage can be turned off. The app is then the default theme, dark.
            logger.LogWarning(exception, "The theme choice could not be read from this device");
            return null;
        }
    }

    #endregion
}
