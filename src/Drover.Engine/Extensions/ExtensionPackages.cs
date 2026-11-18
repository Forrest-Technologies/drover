namespace Drover.Engine.Extensions;

/// <summary>An extension file as it was read: the package, or the sentence that says why it is not one.</summary>
public sealed record ExtensionReading(ExtensionPackage? Package, string? Problem)
{
    /// <summary>The SHA-256 of the file's text, in hex. Empty when the file was refused.</summary>
    public string ContentHash { get; init; } = string.Empty;
}

/// <summary>
/// Reads an extension file. The file is a stranger's text, so the reading is strict: a field or a kind of part this
/// server does not know refuses the whole file, because skipping what is not understood would add something other
/// than what the file says. Nothing here contacts anything.
/// </summary>
public static partial class ExtensionPackages
{
    #region Private Fields

    /// <summary>The most bytes of an extension file. A bot's instructions are the largest thing in one.</summary>
    public const int MaxPackageBytes = 256 * 1024;

    private const int MaxSuggestedTools = 50;

    private const int MaxToolNameLength = 200;

    private const int MaxReasonLength = 500;

    private static readonly string[] knownKinds = [BotTemplate.Kind, ConnectionRecipe.Kind];

    private static readonly string[] privateSuffixes = [".local", ".localhost", ".internal", ".lan", ".home.arpa"];

    private static readonly string[] knownAuth = ["none", "header", "oauth"];

    private static readonly JsonSerializerOptions strict = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        AllowOutOfOrderMetadataProperties = true,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 16,
    };

    #endregion

    #region Public Methods

    public static ExtensionReading Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Refused("Choose an extension file, or paste its text.");
        }

        if (Encoding.UTF8.GetByteCount(text) > MaxPackageBytes)
        {
            return Refused($"This file is larger than an extension can be ({MaxPackageBytes / 1024} KB).");
        }

        try
        {
            if (FindUnreadablePart(text) is { } unreadablePart)
            {
                return Refused(unreadablePart);
            }

            if (JsonSerializer.Deserialize<ExtensionPackage>(text, strict) is not { } package)
            {
                return Refused(NotAnExtension(null));
            }

            return FindProblem(package) is { } problem
                ? Refused(problem)
                : new ExtensionReading(package, null) { ContentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))) };
        }
        catch (JsonException unreadable)
        {
            return Refused(NotAnExtension(unreadable.Path));
        }
    }

    /// <summary>
    /// The text with every character that does not show on a screen written out where it stands, as
    /// <c>[U+200B]</c>, and how many there were. Line breaks and tabs show, so they stay.
    /// </summary>
    public static (string Text, int Hidden) ShowHidden(string text)
    {
        var shown = new StringBuilder(text.Length);
        var hidden = 0;

        var index = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            if (IsHidden(rune))
            {
                hidden++;
                shown.Append(CultureInfo.InvariantCulture, $"[U+{rune.Value:X4}]");
            }
            else
            {
                shown.Append(text.AsSpan(index, rune.Utf16SequenceLength));
            }

            index += rune.Utf16SequenceLength;
        }

        return (shown.ToString(), hidden);
    }

    /// <summary>
    /// True when the address is on the server's own machine or network, or is not https: a loopback or private
    /// address, a name with no dot in it, or a name under a suffix kept for private use. Connecting to one from a
    /// file needs a person's explicit yes, because a file must not quietly aim a server at its own network.
    /// </summary>
    public static bool IsLocal(Uri address) =>
        address.Scheme is not "https"
        || address.IsLoopback
        || !address.Host.Contains('.', StringComparison.Ordinal)
        || privateSuffixes.Any(suffix => address.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        || (IPAddress.TryParse(address.Host.Trim('[', ']'), out var literal) && PrivateAddresses.Contains(literal));

    #endregion

    #region Validation

    private static ExtensionReading Refused(string problem) => new(null, problem);

    private static string NotAnExtension(string? path) =>
        string.IsNullOrEmpty(path) || path is "$"
            ? "This is not an extension file Drover can read."
            : $"This is not an extension file Drover can read. The trouble is at {path}.";

    /// <summary>
    /// What is wrong with the first part that names no kind, or a kind this server has no reader for. Found before
    /// the strict reading, so the refusal can name the kind and a part with none is never read as anything.
    /// </summary>
    private static string? FindUnreadablePart(string text)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = strict.MaxDepth });

        if (document.RootElement.ValueKind is not JsonValueKind.Object
            || !document.RootElement.TryGetProperty("contents", out var contents)
            || contents.ValueKind is not JsonValueKind.Array)
        {
            return null;
        }

        foreach (var (part, index) in contents.EnumerateArray().Select((part, index) => (part, index)))
        {
            var kind = part.ValueKind is JsonValueKind.Object && part.TryGetProperty("kind", out var named) && named.ValueKind is JsonValueKind.String
                ? named.GetString()
                : null;

            if (kind is null)
            {
                return NotAnExtension($"$.contents[{index}]");
            }

            if (!knownKinds.Contains(kind, StringComparer.Ordinal))
            {
                return $"This extension holds a part of the kind \"{kind}\", which this server does not know. Nothing was added.";
            }
        }

        return null;
    }

    private static string? FindProblem(ExtensionPackage package)
    {
        if (package.Version != ExtensionPackage.CurrentVersion)
        {
            return $"This extension is written in version {package.Version} of the format. This server reads version {ExtensionPackage.CurrentVersion}.";
        }

        if (package.Id.Length > ExtensionFile.MaxIdLength || !IdPattern().IsMatch(package.Id))
        {
            return "The extension's id must be 3 to 100 lower-case letters, digits, dots, and hyphens, starting with a letter or digit.";
        }

        if (string.IsNullOrWhiteSpace(package.Name) || package.Name.Length > BotService.MaxNameLength)
        {
            return $"The extension needs a name of up to {BotService.MaxNameLength} characters.";
        }

        if (package.Description.Length > BotService.MaxDescriptionLength)
        {
            return $"The extension's description must be under {BotService.MaxDescriptionLength} characters.";
        }

        var bots = package.Contents.OfType<BotTemplate>().ToList();
        var connections = package.Contents.OfType<ConnectionRecipe>().ToList();

        return package.Contents.Count == 0 ? "This extension adds nothing: it has no contents."
            : bots.Count > 1 ? "An extension adds one bot at most. This one holds more."
            : connections.Count > 1 ? "An extension names one tool server at most. This one names more."
            : bots.Select(FindProblem).Concat(connections.Select(FindProblem)).FirstOrDefault(problem => problem is not null);
    }

    private static string? FindProblem(ConnectionRecipe connection)
    {
        if (ToolConnectionService.FindAddressProblem(connection.Name, connection.Url) is not null)
        {
            return $"The tool server in this extension needs a name of up to {ToolConnectionService.MaxNameLength} characters and a web address that starts with https:// or http://, with no user name, password, \"?\" or \"#\" in it.";
        }

        if (!knownAuth.Contains(connection.Auth, StringComparer.Ordinal))
        {
            return "The tool server in this extension must say how Drover proves who is calling: none, header, or oauth.";
        }

        if (connection.Auth is "header" != connection.HeaderName is not null
            || (connection.HeaderName is not null && ToolConnectionService.FindHeaderNameProblem(connection.HeaderName) is not null))
        {
            return "The tool server in this extension must name its header, such as Authorization or X-Api-Key, when it uses one, and only then.";
        }

        return FindProblem(connection.SuggestedTools);
    }

    private static string? FindProblem(IReadOnlyList<SuggestedTool> tools) =>
        tools.Count > MaxSuggestedTools ? $"An extension suggests {MaxSuggestedTools} tools at most."
        : tools.Any(tool => string.IsNullOrWhiteSpace(tool.Name) || tool.Name.Length > MaxToolNameLength || tool.Name.Any(char.IsControl))
            ? $"Each suggested tool needs the tool's name, up to {MaxToolNameLength} characters."
        : tools.Any(tool => tool.Why.Length > MaxReasonLength) ? $"The reason for a suggested tool must be under {MaxReasonLength} characters."
        : tools.Select(tool => tool.Name).Distinct(StringComparer.Ordinal).Count() != tools.Count ? "An extension suggests each tool once."
        : null;

    private static string? FindProblem(BotTemplate bot) =>
        string.IsNullOrWhiteSpace(bot.Name) || bot.Name.Trim().Length > BotService.MaxNameLength ? $"The bot in this extension needs a name of up to {BotService.MaxNameLength} characters."
        : bot.Description.Length > BotService.MaxDescriptionLength ? $"What the bot in this extension is for must be under {BotService.MaxDescriptionLength} characters."
        : bot.Instructions.Length > BotService.MaxInstructionsLength ? $"The instructions of the bot in this extension must be under {BotService.MaxInstructionsLength} characters."
        : bot.SuggestedModel is { Length: > BotService.MaxModelLength } ? $"The suggested model's name must be under {BotService.MaxModelLength} characters."
        : null;

    private static bool IsHidden(Rune rune) =>
        Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.Control => rune.Value is not ('\n' or '\r' or '\t'),
            UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator => true,
            UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned => true,
            _ => false,
        };

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{2,99}$")]
    private static partial Regex IdPattern();

    #endregion
}
