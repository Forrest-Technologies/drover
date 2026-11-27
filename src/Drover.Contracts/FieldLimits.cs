namespace Drover.Contracts;

/// <summary>
/// The longest text the server takes for a field a person types into. The server's validators read these, and each
/// client sets the same number on the field, so a person is stopped while typing instead of after sending.
/// </summary>
public static class FieldLimits
{
    #region Private Fields

    /// <summary>A bot's name, and an extension's name, which is checked as one.</summary>
    public const int BotName = 200;

    /// <summary>A bot's handle, the name other bots and people address it by.</summary>
    public const int BotHandle = 100;

    /// <summary>What a bot or an extension is for.</summary>
    public const int BotDescription = 2_000;

    /// <summary>The name of a model a bot thinks with.</summary>
    public const int ModelName = 200;

    /// <summary>What a person calls a tool server.</summary>
    public const int ToolServerName = 200;

    /// <summary>The name of the header a tool server reads its key from.</summary>
    public const int HeaderName = 100;

    /// <summary>A person's display name.</summary>
    public const int DisplayName = 200;

    /// <summary>A shell command run in a bot's sandbox.</summary>
    public const int SandboxCommand = 4_000;

    /// <summary>The text or password typed into a sandbox's desktop at once.</summary>
    public const int TypedText = 2_000;

    /// <summary>A currency code, as a price of a model is written in: three letters.</summary>
    public const int CurrencyCode = 3;

    #endregion
}
