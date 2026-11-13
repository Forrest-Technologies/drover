namespace Drover.Ui.Tools;

/// <summary>
/// A tool connection's state in plain words. Every state is said in words; the badge's color only repeats it.
/// </summary>
public static class ConnectionWords
{
    #region Public Methods

    /// <summary>Whether the server answers, as a badge and the words in it.</summary>
    public static (BadgeKind Kind, string Words) Status(ToolConnectionSummary connection) => connection.Status switch
    {
        "ready" => (BadgeKind.Ok, "Connected"),
        "failed" => (BadgeKind.Danger, "Not connected"),
        "disabled" => (BadgeKind.Neutral, "Turned off"),
        _ => (BadgeKind.Neutral, "Not checked yet"),
    };

    /// <summary>What the sign-in needs from the reader, or null when nothing is asked of them.</summary>
    public static string? SignInNeed(ToolConnectionSummary connection) => (connection.SignIn, connection.SignInScope) switch
    {
        ("needs_sign_in", "per_user") => "You have not signed in to this server yet.",
        ("needs_sign_in", _) => "Nobody has signed in to this server for the space yet.",
        ("needs_reconnect", "per_user") => "Your sign-in to this server has lapsed. Sign in again to keep using its tools.",
        ("needs_reconnect", _) => "The space's sign-in to this server has lapsed. Sign in again to keep using its tools.",
        _ => null,
    };

    /// <summary>How the connection proves who is calling, in a few words.</summary>
    public static string Auth(ToolConnectionSummary connection) => (connection.AuthKind, connection.SignInScope) switch
    {
        ("header", _) => "a header",
        ("oauth", "per_user") => "each person signs in",
        ("oauth", _) => "one sign-in for the space",
        _ => "no credential",
    };

    /// <summary>The one line of machine facts under a connection's name.</summary>
    public static string Facts(ToolConnectionSummary connection, IReadOnlyList<ConnectionToolSummary>? tools)
    {
        List<string> facts = [Auth(connection)];

        if (tools is not null)
        {
            var listed = tools.Count(tool => tool.State is "available");
            facts.Add(listed == 1 ? "1 tool" : $"{listed} tools");

            if (tools.Count(tool => tool.NeedsReview) is > 0 and var waiting)
            {
                facts.Add($"{waiting} to review");
            }
        }

        if (connection.LastListedAt is { } at)
        {
            facts.Add($"listed {at.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.CurrentCulture)}");
        }

        return string.Join(" · ", facts);
    }

    #endregion
}
