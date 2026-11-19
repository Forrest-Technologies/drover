namespace Drover.Server.Files;

/// <summary>The two origins that keep the application and untrusted HTML on separate sites.</summary>
public sealed record PreviewOrigin(Uri App, Uri Preview)
{
    #region Constants

    public const string Variable = "DROVER_PREVIEW_URL";

    public const string Setting = "Drover:PreviewUrl";

    #endregion

    #region Public Methods

    public static PreviewOrigin? Read(IConfiguration configuration)
    {
        var value = configuration[Variable] ?? configuration[Setting];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var app = PublicAddress.Read(configuration)
            ?? throw Invalid($"{PublicAddress.Variable} must name the application origin before {Variable} is set.");

        var preview = Parse(value, Variable);
        app = Parse(app.ToString(), PublicAddress.Variable);
        if (Site(app) == Site(preview))
        {
            throw Invalid($"{Variable} must use a separate site from {PublicAddress.Variable}.");
        }

        return new(app, preview);
    }

    public bool IsPreviewHost(HttpRequest request) =>
        string.Equals(request.Host.Host.TrimEnd('.'), Preview.IdnHost, StringComparison.OrdinalIgnoreCase);

    public bool IsPreview(HttpRequest request) =>
        IsPreviewHost(request) && request.IsHttps && (request.Host.Port ?? 443) == Preview.Port;

    #endregion

    #region Private Methods

    private static Uri Parse(string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var address)
            || address is not { Scheme: "https", UserInfo: "", Query: "", Fragment: "" }
            || address.AbsolutePath != "/"
            || IPAddress.TryParse(address.IdnHost, out _)
            || !IsPlainDnsName(address.IdnHost))
        {
            throw Invalid($"{name} must be an HTTPS origin with a DNS name and no path, query, or fragment.");
        }

        return new Uri(address.GetLeftPart(UriPartial.Authority));
    }

    private static bool IsPlainDnsName(string host) =>
        !host.EndsWith('.', StringComparison.Ordinal)
        && host.Split('.', StringSplitOptions.None) is [_, _, ..] labels
        && labels.All(label => label.Length > 0);

    private static string Site(Uri address)
    {
        var labels = address.IdnHost.Split('.');
        return $"{labels[^2]}.{labels[^1]}";
    }

    private static InvalidOperationException Invalid(string message) => new(message);

    #endregion
}
