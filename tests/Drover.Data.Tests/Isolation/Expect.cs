namespace Drover.Data.Tests.Isolation;

/// <summary>Assertions for the generated checks. They throw <see cref="IsolationViolationException"/> with a message that names the table.</summary>
public static class Expect
{
    #region Public Methods

    public static void That(bool condition, string message)
    {
        if (!condition)
        {
            throw new IsolationViolationException(message);
        }
    }

    /// <summary>The statement must fail with <paramref name="sqlState"/>: 42501 is a policy refusing it, 23503 a foreign key.</summary>
    public static async Task Refused(string what, string sqlState, Func<Task> statement)
    {
        var thrown = await Record.ExceptionAsync(statement)
            ?? throw new IsolationViolationException($"{what}: expected SQLSTATE {sqlState}, but the database accepted it.");

        if (thrown is not PostgresException { SqlState: var actual } || actual != sqlState)
        {
            throw new IsolationViolationException($"{what}: expected SQLSTATE {sqlState}, got {thrown.GetType().Name}: {thrown.Message}");
        }
    }

    #endregion
}
