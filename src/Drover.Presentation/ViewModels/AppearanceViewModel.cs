namespace Drover.Presentation.ViewModels;

/// <summary>
/// The Appearance page: "Pick how Drover looks for you, and see it before you keep it." Choosing a theme applies it
/// to the whole app at once; the page then offers the one before until the person leaves. A device keeps its own
/// choice unless the person asks it to use the same theme on all their devices.
/// </summary>
public sealed partial class AppearanceViewModel(
    ThemeModel theme,
    ThemeSync sync,
    AppearanceTrail trail,
    ILogger<AppearanceViewModel> logger) : ObservableObject
{
    #region Private Fields

    private static readonly (ThemeMode Mode, string Name)[] modeNames =
    [
        (ThemeMode.System, "Follow device"),
        (ThemeMode.Dark, "Dark"),
        (ThemeMode.Light, "Light"),
    ];

    private static readonly (ThemeGroup Group, string Name)[] groupNames =
    [
        (ThemeGroup.Expressive, "Expressive"),
        (ThemeGroup.Professional, "Professional"),
    ];

    private IReadOnlyCollection<DeviceRequest> asked = [];

    [ObservableProperty]
    private string? problem;

    [ObservableProperty]
    private bool saving;

    #endregion

    #region Properties

    /// <summary>The themes the app has, in groups, in the order the app lists them. A group with no theme is left out.</summary>
    public IReadOnlyList<ThemeGroupView> Groups =>
    [
        .. groupNames
            .Select(group => new ThemeGroupView(group.Name, [.. BuiltInThemes.All.Where(file => file.Group == group.Group).Select(Option)]))
            .Where(group => group.Themes.Count > 0),
    ];

    public IReadOnlyList<SegmentOption<string>> Modes => [.. modeNames.Select(name => new SegmentOption<string>(name.Mode.ToString(), name.Name))];

    /// <summary>The mode in use, as the value of its option in <see cref="Modes"/>.</summary>
    public string Mode => theme.Choice.Mode.ToString();

    /// <summary>The words of the way back, or null while there is nothing to go back to.</summary>
    public string? BackLabel => trail.Before is { } before && before != theme.Choice ? $"Back to {Describe(before)}" : null;

    public bool UsesEverywhere => sync.Follows;

    /// <summary>What the switch for all devices means, in its current position.</summary>
    public string EverywhereAbout => sync.Follows
        ? "This device uses the theme you keep for all your devices, and a theme you choose here is kept for them."
        : "This device keeps its own theme. Turn this on to use the same theme on every device where it is on.";

    /// <summary>One sentence for each thing the device asks for that a theme suits, naming those themes. Nothing is hidden.</summary>
    public IReadOnlyList<string> Suggestions =>
    [
        .. asked.Select(request => (Request: request, Themes: BuiltInThemes.All.Where(file => file.Suits.Contains(request)).Select(file => file.Name).ToList()))
            .Where(pair => pair.Themes.Count > 0)
            .Select(pair => $"{Asked(pair.Request)} {Joined(pair.Themes)} {(pair.Themes.Count == 1 ? "suits" : "suit")} that."),
    ];

    #endregion

    #region Public Methods

    /// <summary>What the device asks for, which the platform reads. The page words the themes that suit it.</summary>
    public void DeviceAsks(IReadOnlyCollection<DeviceRequest> requests)
    {
        asked = requests;
        OnPropertyChanged(nameof(Suggestions));
    }

    /// <summary>Applies a theme to the whole app at once, in the mode in use.</summary>
    [RelayCommand]
    public void ChooseTheme(string themeId) => Apply(theme.Choice with { ThemeId = themeId });

    /// <summary>Takes a mode of the theme in use: the value of an option in <see cref="Modes"/>.</summary>
    [RelayCommand]
    public void ChooseMode(string mode)
    {
        if (Enum.TryParse<ThemeMode>(mode, out var chosen) && Enum.IsDefined(chosen))
        {
            Apply(theme.Choice with { Mode = chosen });
        }
    }

    [RelayCommand]
    public void GoBack()
    {
        if (trail.Before is { } before)
        {
            Apply(before);
        }
    }

    /// <summary>Turns the use of one theme on all devices on or off for this device.</summary>
    public async Task SetEverywhere(bool everywhere)
    {
        Problem = null;

        if (!everywhere)
        {
            sync.KeepOnThisDevice();
            Changed();
            return;
        }

        Saving = true;

        try
        {
            await sync.UseEverywhere();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(AppearanceViewModel));
            Problem = $"Your theme was not kept for your other devices. {Failures.Sentence(exception, "Check that the server is running, then try again.")}";
        }
        finally
        {
            Saving = false;
            Changed();
        }
    }

    #endregion

    #region Private Methods

    private void Apply(ThemeChoice choice)
    {
        if (choice == theme.Choice)
        {
            return;
        }

        trail.Leaving(theme.Choice);
        theme.Choose(choice);
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(BackLabel));
        OnPropertyChanged(nameof(UsesEverywhere));
        OnPropertyChanged(nameof(EverywhereAbout));
    }

    private ThemeOptionView Option(ThemeFile file) => new(file, file.Id == theme.Theme.Id);

    private static string Describe(ThemeChoice choice)
    {
        var name = BuiltInThemes.Get(choice.ThemeId)?.Name ?? BuiltInThemes.NightYard.Name;
        var mode = choice.Mode switch
        {
            ThemeMode.System => "following the device",
            ThemeMode.Light => "light",
            _ => "dark",
        };

        return $"{name}, {mode}";
    }

    private static string Asked(DeviceRequest request) => request switch
    {
        DeviceRequest.LargeText => "This device asks for larger text.",
        _ => "This device asks for less motion.",
    };

    private static string Joined(List<string> names) => names.Count switch
    {
        1 => names[0],
        _ => $"{string.Join(", ", names[..^1])} and {names[^1]}",
    };

    #endregion
}

/// <summary>One group of themes on the Appearance page: its heading and its themes.</summary>
public sealed record ThemeGroupView(string Heading, IReadOnlyList<ThemeOptionView> Themes);

/// <summary>One theme on the Appearance page, and whether it is the one in use.</summary>
public sealed record ThemeOptionView(ThemeFile Theme, bool InUse)
{
    /// <summary>What a screen reader is told: the name and the intent. Whether it is chosen is its own state.</summary>
    public string AccessibleName => $"{Theme.Name}, {Theme.Intent}";
}
