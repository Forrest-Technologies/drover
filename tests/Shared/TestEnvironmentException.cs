namespace Drover.Tests.Common;

/// <summary>
/// A test cannot start because the machine has no browser or PostgreSQL for it. The message says what was tried and
/// how to fix it; the original failure stays on <see cref="Exception.InnerException"/> for diagnosis.
/// </summary>
public sealed class TestEnvironmentException(string message, Exception? innerException = null)
    : Exception(message, innerException);
