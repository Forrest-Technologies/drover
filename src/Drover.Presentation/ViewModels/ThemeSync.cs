namespace Drover.Presentation.ViewModels;

/// <summary>
/// Whether this device uses the theme the person keeps for all their devices. A device keeps its own choice by
/// default. Once the person asks it to use the same theme everywhere, it takes the kept theme each time they sign in,
/// and a theme chosen on it is kept for the others. The choice to follow is this device's, kept beside the theme.
/// </summary>
public sealed partial class ThemeSync : ObservableObject, IDisposable
{
    #region Private Fields

    /// <summary>The name of this device's choice to follow the account.</summary>
    public const string SettingName = "drover.theme.everywhere";

    private const string On = "on";

    private readonly ThemeModel theme;

    private readonly SessionState session;

    private readonly DroverApi api;

    private readonly IDeviceSettings settings;

    private readonly ILogger<ThemeSync> logger;

    private bool taking;

    [ObservableProperty]
    private bool follows;

    #endregion

    #region Constructors

    // A traditional constructor, because the sync listens to the theme and the session from the moment it exists.
#pragma warning disable IDE0290
    public ThemeSync(
        ThemeModel theme,
        SessionState session,
        DroverApi api,
        IDeviceSettings settings,
        ILogger<ThemeSync> logger)
#pragma warning restore IDE0290
    {
        (this.theme, this.session, this.api, this.settings, this.logger) = (theme, session, api, settings, logger);
        follows = Stored() == On;
        theme.PropertyChanged += ThemeChanged;
        session.Changed += SessionChanged;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Keeps the theme in use for all the person's devices, and has this device follow it. Throws what the request
    /// threw, so the page can say what failed; nothing changes on the device then.
    /// </summary>
    public async Task UseEverywhere(CancellationToken cancellationToken = default)
    {
        await api.SetAppearance(theme.Choice.ToSetting(), cancellationToken);
        Keep(true);
    }

    /// <summary>This device keeps its own theme from now on. The theme kept for the others stays as it is.</summary>
    public void KeepOnThisDevice() => Keep(false);

    /// <summary>
    /// Takes the theme kept for the person's devices, when this device follows it and the person is signed in.
    /// </summary>
    public async Task TakeKept(CancellationToken cancellationToken = default)
    {
        if (!Follows || session.Current is null)
        {
            return;
        }

        try
        {
            if (await api.GetAppearance(cancellationToken) is { } kept
                && kept.ToChoice() is { } choice
                && choice != theme.Choice)
            {
                taking = true;
                theme.Choose(choice);
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            // The device keeps the theme it shows. It tries again the next time the person signs in.
            logger.LogWarning(exception, "The theme kept for all devices could not be read");
        }
        finally
        {
            taking = false;
        }
    }

    public void Dispose()
    {
        theme.PropertyChanged -= ThemeChanged;
        session.Changed -= SessionChanged;
    }

    #endregion

    #region Private Methods

    private void Keep(bool everywhere)
    {
        Follows = everywhere;

        try
        {
            settings.Write(SettingName, everywhere ? On : "off");
        }
        catch (InvalidOperationException exception)
        {
            // Storage can be turned off. The choice still holds for this visit.
            logger.LogWarning(exception, "The choice to use one theme everywhere could not be kept on this device");
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
            logger.LogWarning(exception, "The choice to use one theme everywhere could not be read from this device");
            return null;
        }
    }

    private void SessionChanged() => _ = TakeKept();

    private void ThemeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (Follows && !taking && session.Current is not null)
        {
            _ = KeepForOthers(theme.Choice);
        }
    }

    // A theme chosen on a device that follows the account is kept for the others. A failure leaves this device as it
    // is and is logged: the page says so the next time the person asks for one theme everywhere.
    private async Task KeepForOthers(ThemeChoice choice)
    {
        try
        {
            await api.SetAppearance(choice.ToSetting());
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "The theme chosen on this device could not be kept for the others");
        }
    }

    #endregion
}
