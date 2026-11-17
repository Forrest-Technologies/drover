namespace Drover.Presentation.Tests.Support;

/// <summary>Sample people and sessions. Every name and value is made up.</summary>
public static class Samples
{
    public static SessionResponse Session(string role = "owner") =>
        new(Guid.Parse("00000000-0000-7000-8000-0000000000a1"), "sample@example.test", "Sample person",
            [new(Guid.Parse("00000000-0000-7000-8000-0000000000b1"), "personal", "Sample space", "sample", role)]);
}
