namespace Drover.Contracts.Auth;

/// <summary>The opaque credential a native client presents to refresh its session.</summary>
public sealed record NativeSessionRefresh(string RefreshToken)
{
    public override string ToString() => nameof(NativeSessionRefresh);
}
