namespace Drover.Data.Tests.Isolation;

/// <summary>The database let something happen that tenant isolation forbids, or refused something it must allow.</summary>
public sealed class IsolationViolationException(string message) : Exception(message)
{
}
