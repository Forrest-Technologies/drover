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

    /// <summary>The most bytes of an extension file. Its themes are the largest things in one.</summary>
    public const int MaxPackageBytes = 256 * 1024;

    /// <summary>The most bytes of one theme in an extension, the size a theme file is held to anywhere.</summary>
    public const int MaxThemeBytes = 32 * 1024;

    public const int MaxThemes = 8;

    public const int MaxRules = 20;

    public const int MaxSettings = 20;

    public const int MaxSettingValueLength = 200;

    private const int MaxChoices = 50;

    private const int MaxLabelLength = 100;

    private const int MaxHelpLength = 500;

    private const int MaxSuggestedTools = 50;

    private const int MaxToolNameLength = 200;

    private const int MaxReasonLength = 500;

    private const int FirstVersion = 1;

    private const string BotKind = "bot";

    private static readonly string[] knownKinds = [ConnectionRecipe.Kind, ThemePart.Kind, RuleTemplate.Kind];

    private static readonly string[] settingTypes =
        [ExtensionSetting.Text, ExtensionSetting.Number, ExtensionSetting.Choice, ExtensionSetting.Toggle, ExtensionSetting.Secret];

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

        var read = Read(text, kept: false);
        return read.Package is null ? read : read with { ContentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))) };
    }

    /// <summary>
    /// The package of a file this space imported earlier. A file kept from the first version of the format, when an
    /// extension could hold a bot, is read as what is left of it: its bot became an ordinary bot of the space when it
    /// was imported, so the part is dropped, and what remains may be nothing. Null when the kept file cannot be read.
    /// </summary>
    public static ExtensionPackage? ReadKept(JsonElement kept)
    {
        if (kept.ValueKind is not JsonValueKind.Object
            || !kept.TryGetProperty("version", out var version)
            || !version.TryGetInt32(out var written)
            || written is not FirstVersion)
        {
            return Read(kept.GetRawText(), kept: true).Package;
        }

        var file = JsonNode.Parse(kept.GetRawText())!.AsObject();
        file["version"] = ExtensionPackage.CurrentVersion;

        if (file["contents"] is JsonArray contents)
        {
            foreach (var bot in contents.Where(part => part is JsonObject && (string?)part["kind"] is BotKind).ToList())
            {
                contents.Remove(bot);
            }
        }

        return Read(file.ToJsonString(), kept: true).Package;
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

    /// <summary>The keys of the settings the text uses, written in it as <c>{key}</c>.</summary>
    public static IReadOnlyList<string> SettingsIn(string? text) =>
        text is null ? [] : [.. Placeholder().Matches(text).Select(found => found.Groups[1].Value).Distinct(StringComparer.Ordinal)];

    /// <summary>The text with each <c>{key}</c> replaced by its value. A key with no value stays as it is written.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(text, found => values.TryGetValue(found.Groups[1].Value, out var value) ? value : found.Value);

    /// <summary>What is wrong with a value a person gave a setting, or null.</summary>
    public static string? FindValueProblem(ExtensionSetting setting, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return $"Fill in {setting.Label}.";
        }

        return setting.Type switch
        {
            ExtensionSetting.Secret when value.Length > ToolConnectionService.MaxHeaderValueLength =>
                $"{setting.Label} must be under {ToolConnectionService.MaxHeaderValueLength} characters.",
            ExtensionSetting.Secret => null,
            _ when value.Length > MaxSettingValueLength || value.Any(char.IsControl) =>
                $"{setting.Label} must be one line of up to {MaxSettingValueLength} characters.",
            ExtensionSetting.Number when !decimal.TryParse(value, NumberStyles.Number & ~NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _) =>
                $"{setting.Label} must be a number.",
            ExtensionSetting.Toggle when value is not ("true" or "false") => $"{setting.Label} must be true or false.",
            ExtensionSetting.Choice when !setting.Choices.Contains(value, StringComparer.Ordinal) => $"{setting.Label} must be one of its choices.",
            _ => null,
        };
    }

    #endregion

    #region Reading

    private static ExtensionReading Refused(string problem) => new(null, problem);

    private static ExtensionReading Read(string text, bool kept)
    {
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

            return FindProblem(package, kept) is { } problem ? Refused(problem) : new ExtensionReading(package, null);
        }
        catch (JsonException unreadable)
        {
            return Refused(NotAnExtension(unreadable.Path));
        }
    }

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

            if (kind is BotKind)
            {
                return "This extension holds a bot, and an extension no longer adds one. Nothing was added. A bot is shared as a bot file, from the bot's own menu, and imported on the Bots screen.";
            }

            if (!knownKinds.Contains(kind, StringComparer.Ordinal))
            {
                return $"This extension holds a part of the kind \"{kind}\", which this server does not know. Nothing was added.";
            }
        }

        return null;
    }

    #endregion

    #region Validation

    private static string? FindProblem(ExtensionPackage package, bool kept)
    {
        if (package.Version != ExtensionPackage.CurrentVersion)
        {
            return $"This extension is written in version {package.Version} of the format. This server reads version {ExtensionPackage.CurrentVersion}.";
        }

        if (package.Id.Length > ExtensionPackage.MaxIdLength || !IdPattern().IsMatch(package.Id))
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

        var connections = package.Contents.OfType<ConnectionRecipe>().ToList();
        var themes = package.Contents.OfType<ThemePart>().ToList();
        var rules = package.Contents.OfType<RuleTemplate>().ToList();

        return package.Contents.Count == 0 && !kept ? "This extension adds nothing: it has no contents."
            : connections.Count > 1 ? "An extension names one tool server at most. This one names more."
            : themes.Count > MaxThemes ? $"An extension offers {MaxThemes} themes at most."
            : rules.Count > MaxRules ? $"An extension offers {MaxRules} rules at most."
            : connections.Select(FindProblem).FirstOrDefault(problem => problem is not null)
                ?? FindSettingsProblem(package.Settings, connections.SingleOrDefault())
                ?? FindThemesProblem(themes)
                ?? FindRulesProblem(rules);
    }

    private static string? FindProblem(ConnectionRecipe connection)
    {
        // A setting in the address is checked as a digit standing in its place, which fits a name, a port, and a path. The address is checked again, whole, when a person connects.
        if (ToolConnectionService.FindAddressProblem(connection.Name, Placeholder().Replace(connection.Url, "1")) is not null)
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

        if (connection.HeaderTemplate is not null && connection.Auth is not "header")
        {
            return "The tool server in this extension gives a header value, and uses no header.";
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

    /// <summary>
    /// A setting exists to be used: each one the file declares is written somewhere as <c>{key}</c>, and each
    /// <c>{key}</c> names a declared one. A secret goes only into the header's value, never into an address, which
    /// every member can read. A header's value in a file must hold a secret setting, so a file never carries the
    /// credential itself.
    /// </summary>
    private static string? FindSettingsProblem(IReadOnlyList<ExtensionSetting> settings, ConnectionRecipe? connection)
    {
        if (settings.Count > MaxSettings)
        {
            return $"An extension has {MaxSettings} settings at most.";
        }

        if (settings.Select(FindProblem).FirstOrDefault(problem => problem is not null) is { } problem)
        {
            return problem;
        }

        if (settings.Select(setting => setting.Key).Distinct(StringComparer.Ordinal).Count() != settings.Count)
        {
            return "An extension names each setting once.";
        }

        var inAddress = SettingsIn(connection?.Url);
        var inHeader = SettingsIn(connection?.HeaderTemplate);
        var secrets = settings.Where(setting => setting.Type is ExtensionSetting.Secret).Select(setting => setting.Key).ToList();

        return inAddress.Concat(inHeader).FirstOrDefault(key => settings.All(setting => setting.Key != key)) is { } unknown
                ? $"This extension uses a setting named \"{unknown}\" that it does not declare."
            : settings.FirstOrDefault(setting => !inAddress.Contains(setting.Key) && !inHeader.Contains(setting.Key)) is { } unused
                ? $"This extension declares the setting \"{unused.Key}\" and nothing in it uses it."
            : inAddress.FirstOrDefault(secrets.Contains) is { } exposed
                ? $"This extension puts the secret setting \"{exposed}\" in an address. A secret goes only in a header's value."
            : connection?.HeaderTemplate is not null && !inHeader.Any(secrets.Contains)
                ? "The header value in this extension must hold a secret setting, such as \"Bearer {token}\". A file never carries the credential itself."
            : null;
    }

    private static string? FindProblem(ExtensionSetting setting)
    {
        if (!SettingKey().IsMatch(setting.Key))
        {
            return "A setting's key must be 1 to 40 lower-case letters, digits, and hyphens, starting with a letter.";
        }

        if (string.IsNullOrWhiteSpace(setting.Label) || setting.Label.Length > MaxLabelLength || setting.Help.Length > MaxHelpLength)
        {
            return $"The setting \"{setting.Key}\" needs a label of up to {MaxLabelLength} characters, and help under {MaxHelpLength}.";
        }

        if (!settingTypes.Contains(setting.Type, StringComparer.Ordinal))
        {
            return $"The setting \"{setting.Key}\" must be of the type text, number, choice, toggle, or secret.";
        }

        var isChoice = setting.Type is ExtensionSetting.Choice;

        if (isChoice != setting.Choices.Count > 0
            || setting.Choices.Count > MaxChoices
            || setting.Choices.Any(choice => string.IsNullOrWhiteSpace(choice) || choice.Length > MaxLabelLength || choice.Any(char.IsControl))
            || setting.Choices.Distinct(StringComparer.Ordinal).Count() != setting.Choices.Count)
        {
            return $"The setting \"{setting.Key}\" must list its choices, up to {MaxChoices}, each once, when it is a choice, and only then.";
        }

        return setting.Default is null ? null
            : setting.Type is ExtensionSetting.Secret ? $"The secret setting \"{setting.Key}\" cannot have a value in the file."
            : FindValueProblem(setting, setting.Default) is not null ? $"The setting \"{setting.Key}\" starts with a value it does not accept."
            : null;
    }

    /// <summary>Each theme is held to the checker every theme passes, so a file cannot bring a look that fails it.</summary>
    private static string? FindThemesProblem(List<ThemePart> themes)
    {
        HashSet<string> seen = [];

        foreach (var part in themes)
        {
            var id = part.Theme.ValueKind is JsonValueKind.Object && part.Theme.TryGetProperty("id", out var named) && named.ValueKind is JsonValueKind.String
                ? named.GetString()!
                : null;
            var text = part.Theme.GetRawText();

            if (id is null)
            {
                return "A theme in this extension is not a theme file: it has no id.";
            }

            if (Encoding.UTF8.GetByteCount(text) > MaxThemeBytes)
            {
                return $"The theme \"{id}\" in this extension is larger than a theme can be ({MaxThemeBytes / 1024} KB).";
            }

            if (ThemeIds.Reserved.Contains(id, StringComparer.Ordinal))
            {
                return $"The theme id \"{id}\" is one of Drover's own. A theme in an extension needs another id.";
            }

            if (!seen.Add(id))
            {
                return $"This extension holds the theme \"{id}\" more than once.";
            }

            if (ThemeGates.CheckFile(text, id) is [var failure, ..])
            {
                return $"The theme \"{id}\" in this extension does not pass the theme checks: {failure.What}.";
            }
        }

        return null;
    }

    private static string? FindRulesProblem(List<RuleTemplate> rules)
    {
        foreach (var rule in rules)
        {
            if (RuleService.FindProblem(rule.ToBody()) is { } problem)
            {
                return $"A rule in this extension cannot be read. {problem}";
            }
        }

        return rules.Select(rule => rule.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rules.Count
            ? "An extension names each rule once."
            : null;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{2,99}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[a-z][a-z0-9-]{0,39}$")]
    private static partial Regex SettingKey();

    [GeneratedRegex(@"\{([a-z][a-z0-9-]{0,39})\}")]
    private static partial Regex Placeholder();

    #endregion
}
