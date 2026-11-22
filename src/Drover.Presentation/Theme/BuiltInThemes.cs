namespace Drover.Ui.Theme;

/// <summary>
/// The themes that ship inside the app, each read once from its file in <c>Theme/Themes</c> the first time it is
/// asked for. Only Night Yard ships through the pipeline so far: a theme joins this list when the issue that designs
/// it embeds its file, and nothing else names it.
/// </summary>
public static class BuiltInThemes
{
    #region Private Fields

    /// <summary>The id of the default theme and the identity of the product.</summary>
    public const string DefaultId = "night-yard";

    private static readonly string[] ids = [DefaultId];

    private static readonly Lazy<IReadOnlyDictionary<string, ThemeFile>> themes = new(Load);

    private static readonly ConcurrentDictionary<string, ThemeFile> samples = new();

    #endregion

    #region Properties

    public static ThemeFile NightYard => themes.Value[DefaultId];

    public static IReadOnlyList<ThemeFile> All => [.. themes.Value.Values];

    #endregion

    #region Public Methods

    /// <summary>The theme with this id, or null when the app has none by that name.</summary>
    public static ThemeFile? Get(string id) => themes.Value.GetValueOrDefault(id) ?? samples.GetValueOrDefault(id);

    /// <summary>
    /// Makes a theme that is not part of the app known by its id, for the screen checks that switch themes. It is
    /// not in <see cref="All"/>, and nothing in a shipped build calls this.
    /// </summary>
    internal static void AddSample(ThemeFile sample) => samples[sample.Id] = sample;

    #endregion

    #region Private Methods

    private static Dictionary<string, ThemeFile> Load() =>
        ids.ToDictionary(id => id, id => ThemeFileReader.Read(ReadResource(id), id));

    private static string ReadResource(string id)
    {
        using var stream = typeof(BuiltInThemes).Assembly.GetManifestResourceStream($"Drover.Themes.{id}.json")
            ?? throw new InvalidOperationException($"The theme file for '{id}' is not part of the app.");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    #endregion
}
