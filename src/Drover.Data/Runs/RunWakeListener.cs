namespace Drover.Data.Runs;

/// <summary>
/// Listens for the notification a queued run sends, so a second server wakes as fast as the one that took the
/// request. It holds one connection of its own with no transaction and reads no table: a notification carries no
/// tenant data, only "look now". A lost notification costs a poll interval, never correctness.
/// </summary>
public sealed class RunWakeListener(string connectionString, ILogger<RunWakeListener> logger)
{
    #region Private Fields

    private static readonly TimeSpan retryDelay = TimeSpan.FromSeconds(5);

    /// <summary>How often the wait is renewed so stopping is checked even if the provider does not cancel a read.</summary>
    private static readonly TimeSpan waitSlice = TimeSpan.FromSeconds(1);

    #endregion

    #region Public Methods

    /// <summary>Calls <paramref name="wake"/> for every notification until cancelled, reconnecting when the connection drops.</summary>
    public async Task Listen(Action wake, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ListenOnce(wake, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Stopping is a normal end, not a dropped connection. The dispatcher that owns this loop waits for
                // it on shutdown, so it must finish without faulting or waiting on the provider to cancel a read.
                logger.LogDebug("The run notification listener stopped with the server");
                return;
            }
            catch (Exception exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                logger.LogWarning(exception, "The run notification connection dropped; reconnecting. Polling covers the gap");
                await Task.Delay(retryDelay, cancellationToken);
            }
        }
    }

    #endregion

    #region Private Methods

    private async Task ListenOnce(Action wake, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var listen = new NpgsqlCommand($"LISTEN {RunRepository.WakeChannel}", connection);
        await listen.ExecuteNonQueryAsync(cancellationToken);

        // Anything queued while this server was not listening is waiting in the table, so look once now.
        wake();

        while (!cancellationToken.IsCancellationRequested)
        {
            // A bounded wait makes the loop check stopping again even when the provider cannot cancel a read that
            // is already waiting for a notification. A slice that ends with no message is not an error: the loop
            // simply waits again, and stopping is seen at the next check.
            await connection.WaitAsync(waitSlice, cancellationToken);
            wake();
        }
    }

    #endregion
}
