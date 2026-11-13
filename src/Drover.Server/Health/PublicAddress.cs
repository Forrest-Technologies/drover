namespace Drover.Server.Health;

/// <summary>
/// The address people open Drover at, from <c>DROVER_PUBLIC_URL</c> or <c>Drover:PublicUrl</c>. A value that is
/// empty or not an absolute address counts as not set, so a compose file can pass the variable through unset.
/// </summary>
public static class PublicAddress
{
    #region Public Methods

    public static Uri? Read(IConfiguration configuration) =>
        new[] { configuration[Variable], configuration[Setting] }
            .Select(value => Uri.TryCreate(value, UriKind.Absolute, out var address) ? address : null)
            .FirstOrDefault(address => address is { Scheme: "https" or "http" });

    #endregion

    #region Constants

    public const string Variable = "DROVER_PUBLIC_URL";

    public const string Setting = "Drover:PublicUrl";

    #endregion
}

/// <summary>Where the key ring is kept, so the status page can say whether it is there and writable.</summary>
public sealed record KeyRingLocation(string Path);
