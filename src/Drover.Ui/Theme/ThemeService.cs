namespace Drover.Ui.Theme;

/// <summary>
/// Reads and applies the theme choice. The choice is kept on the device, and the page follows the system
/// until a person picks dark or light.
/// </summary>
public sealed class ThemeService(IJSRuntime js, ILogger<ThemeService> logger) : IAsyncDisposable
{
    #region Private Fields

    private const string ModulePath = "./_content/Drover.Ui/theme.js";

    private Task<IJSObjectReference>? module;

    #endregion

    #region Properties

    public ThemeChoice Choice { get; private set; } = ThemeChoice.System;

    #endregion

    #region Public Methods

    public async Task Load()
    {
        var stored = await Call<string>("read");
        Choice = Enum.TryParse<ThemeChoice>(stored, ignoreCase: true, out var choice) ? choice : ThemeChoice.System;
    }

    public async Task Choose(ThemeChoice choice)
    {
        Choice = choice;
        await Call<string>("apply", choice.ToString().ToLowerInvariant());
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            var loaded = await module;
            await loaded.DisposeAsync();
        }
    }

    #endregion

    #region Private Methods

    private async Task<T> Call<T>(string function, params object?[] arguments)
    {
        try
        {
            module ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();
            var loaded = await module;
            return await loaded.InvokeAsync<T>(function, arguments);
        }
        catch (JSException exception)
        {
            logger.LogError(exception, "The theme script failed to {Function}", function);
            throw;
        }
    }

    #endregion
}
