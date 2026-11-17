namespace Drover.Ui.Bots;

/// <summary>
/// A model of the space that can read a bot's screen for it: one the catalog lists as reading images, with the name
/// of its connection, as the Sandbox section offers it.
/// </summary>
public sealed record ScreenReaderChoice(Guid ConnectionId, string ConnectionName, string Model, string DisplayName)
{
    #region Properties

    /// <summary>The choice as one select value. A connection id holds no slash, so the model name may.</summary>
    public string Value => ValueOf(ConnectionId, Model);

    #endregion

    #region Public Methods

    public static string ValueOf(Guid connectionId, string model) => $"{connectionId}/{model}";

    /// <summary>The reader a select value names, or null for the bot's own model.</summary>
    public static ScreenReaderBody? Read(string value) =>
        value.Split('/', 2) is [var connection, var model] && Guid.TryParse(connection, out var connectionId) && model.Length > 0
            ? new ScreenReaderBody(connectionId, model)
            : null;

    #endregion
}
