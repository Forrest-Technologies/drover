namespace Drover.Engine.Files;

/// <summary>Bounds live native image decoders and returns when an independent wall-clock deadline expires.</summary>
internal static class NativeImageDecodeGuard
{
    #region Private Fields

    private const int MaxLiveDecodes = 2;

    private static readonly TimeSpan defaultDeadline = TimeSpan.FromSeconds(2);

    private static int liveDecodes;

    #endregion

    #region Public Methods

    public static async Task<NativeFilePreview> Run(
        Func<NativeFilePreview> decode,
        ILogger logger,
        TimeProvider time,
        TimeSpan? deadline = null)
    {
        var effectiveDeadline = deadline ?? defaultDeadline;
        if (effectiveDeadline <= TimeSpan.Zero || !TryStartDecode())
        {
            return new(Refusal: NativePreviewRefusal.TooComplex);
        }

        var decodeTask = StartDecode(decode);
        using var deadlineCancellation = new CancellationTokenSource();
        var elapsed = Task.Delay(effectiveDeadline, time, deadlineCancellation.Token);
        var completed = await Task.WhenAny(decodeTask, elapsed);
        await deadlineCancellation.CancelAsync();
        if (completed == decodeTask)
        {
            return await decodeTask;
        }

        _ = ObserveFailure(decodeTask, logger);
        return new(Refusal: NativePreviewRefusal.TooComplex);
    }

    #endregion

    #region Private Methods

    private static Task<NativeFilePreview> StartDecode(Func<NativeFilePreview> decode)
    {
        try
        {
            return Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        return decode();
                    }
                    finally
                    {
                        Interlocked.Decrement(ref liveDecodes);
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
        catch
        {
            Interlocked.Decrement(ref liveDecodes);
            throw;
        }
    }

    private static bool TryStartDecode()
    {
        while (true)
        {
            var current = Volatile.Read(ref liveDecodes);
            if (current >= MaxLiveDecodes)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref liveDecodes, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    private static async Task ObserveFailure(Task<NativeFilePreview> decodeTask, ILogger logger)
    {
        try
        {
            await decodeTask;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Picture preview decoder failed after its deadline with {ExceptionType}",
                exception.GetType().Name);
        }
    }

    #endregion
}
