namespace Drover.Server.Accounts;

/// <summary>The opaque credential a native client presents to refresh its session.</summary>
internal sealed record NativeSessionRefresh(string RefreshToken)
{
    public override string ToString() => nameof(NativeSessionRefresh);
}
