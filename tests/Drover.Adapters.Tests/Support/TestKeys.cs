namespace Drover.Adapters.Tests.Support;

/// <summary>Tests make up their own keys. No real credential is ever written down.</summary>
public static class TestKeys
{
    public static string Generate() => $"test-key-{Guid.NewGuid():N}";
}
