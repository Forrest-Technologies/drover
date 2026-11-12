namespace Drover.Engine.Tools;

/// <summary>The built-in tools Phase 0 ships, so a bot's tool loop can be used before any tool server is connected.</summary>
public static class BuiltinTools
{
    #region Properties

    /// <summary>
    /// The built-in tools a new bot is granted, as allow, when a person creates it. A list of names on purpose, not
    /// "every tool that only reads": a later built-in that reads the web, a file, or memory must not reach every new
    /// bot by default. Adding a name here is a product decision.
    /// </summary>
    public static IReadOnlyList<string> GrantedToNewBots { get; } = ["time.now"];

    #endregion

    #region Public Methods

    public static IServiceCollection AddBuiltinTools(this IServiceCollection services) =>
        services.AddSingleton(provider => TimeNow(provider.GetRequiredService<TimeProvider>()));

    /// <summary><c>time.now</c>: the current time, in UTC and in a time zone the model may name. Reads nothing else.</summary>
    public static BuiltinTool TimeNow(TimeProvider time) =>
        new(
            "time.now",
            "Current time",
            "Returns the current date and time in UTC, and in a time zone when one is given by its IANA name, for example Europe/Berlin.",
            JsonElement.Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "time_zone": { "type": "string", "description": "An IANA time zone name, for example America/New_York." }
                  }
                }
                """),
            ToolSensitivity.Read,
            (arguments, _) => Task.FromResult(ReadTime(time, arguments)));

    #endregion

    #region Private Methods

    private static JsonElement ReadTime(TimeProvider time, JsonElement arguments)
    {
        var now = time.GetUtcNow();
        var zoneName = arguments.ValueKind is JsonValueKind.Object
            && arguments.TryGetProperty("time_zone", out var named) && named.ValueKind is JsonValueKind.String
                ? named.GetString()
                : null;

        if (string.IsNullOrWhiteSpace(zoneName))
        {
            return JsonSerializer.SerializeToElement(new { utc = now.ToString("O", CultureInfo.InvariantCulture) });
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zoneName, out var zone))
        {
            return JsonSerializer.SerializeToElement(new
            {
                utc = now.ToString("O", CultureInfo.InvariantCulture),
                error = $"The time zone {zoneName} is not known. Use an IANA name such as Europe/Berlin.",
            });
        }

        return JsonSerializer.SerializeToElement(new
        {
            utc = now.ToString("O", CultureInfo.InvariantCulture),
            local = TimeZoneInfo.ConvertTime(now, zone).ToString("O", CultureInfo.InvariantCulture),
            time_zone = zone.Id,
        });
    }

    #endregion
}
