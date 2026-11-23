namespace Drover.Engine.Tests.Files;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NativeImageDecodeGuardGroup
{
    public const string Name = "Native image decode guard";
}

[Collection(NativeImageDecodeGuardGroup.Name)]
public sealed class NativeImageDecodeGuardTests
{
    #region Tests

    [Fact]
    public async Task Deadline_releases_the_caller_while_live_work_remains_bounded_until_it_exits()
    {
        using var firstRelease = new ManualResetEventSlim(false);
        using var secondRelease = new ManualResetEventSlim(false);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFailure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new LateFailureLog(firstFailure);
        var thirdStarted = false;

        try
        {
            var first = NativeImageDecodeGuard.Run(
                () => BlockThenFail(firstEntered, firstRelease),
                logger,
                TimeProvider.System,
                TimeSpan.FromMilliseconds(100));
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            var firstResult = await first.WaitAsync(
                TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            firstResult.Refusal.Should().Be(NativePreviewRefusal.TooComplex);

            var second = NativeImageDecodeGuard.Run(
                () => BlockThenFinish(secondEntered, secondFinished, secondRelease),
                NullLogger.Instance,
                TimeProvider.System,
                TimeSpan.FromMilliseconds(100));
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            var secondResult = await second.WaitAsync(
                TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            secondResult.Refusal.Should().Be(NativePreviewRefusal.TooComplex);

            var overLimit = await NativeImageDecodeGuard.Run(
                () =>
                {
                    thirdStarted = true;
                    return new();
                },
                NullLogger.Instance,
                TimeProvider.System);

            overLimit.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
            thirdStarted.Should().BeFalse();
        }
        finally
        {
            firstRelease.Set();
            secondRelease.Set();
        }

        (await firstFailure.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken))
            .Should().Contain(nameof(InvalidOperationException));
        await secondFinished.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var afterRelease = await WaitForReleasedSlot();
        afterRelease.Refusal.Should().BeNull();
    }

    #endregion

    #region Private Methods

    private static NativeFilePreview BlockThenFail(
        TaskCompletionSource entered,
        ManualResetEventSlim release)
    {
        entered.TrySetResult();
        release.Wait(TimeSpan.FromSeconds(10));
        throw new InvalidOperationException();
    }

    private static NativeFilePreview BlockThenFinish(
        TaskCompletionSource entered,
        TaskCompletionSource finished,
        ManualResetEventSlim release)
    {
        entered.TrySetResult();
        release.Wait(TimeSpan.FromSeconds(10));
        finished.TrySetResult();
        return new();
    }

    private static async Task<NativeFilePreview> WaitForReleasedSlot()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var started = false;
            var preview = await NativeImageDecodeGuard.Run(
                () =>
                {
                    started = true;
                    return new();
                },
                NullLogger.Instance,
                TimeProvider.System);
            if (started)
            {
                return preview;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }

        return new(Refusal: NativePreviewRefusal.TooComplex);
    }

    #endregion

    #region Test Doubles

    private sealed class LateFailureLog(TaskCompletionSource<string> message) : ILogger
    {
        #region Interface Implementations

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => message.TrySetResult(formatter(state, exception));

        #endregion
    }

    #endregion
}
