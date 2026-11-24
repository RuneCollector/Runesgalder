namespace Runesgalder.FlowTests;

public sealed class RetryPredicateTests
{
    [Fact]
    public async Task RetriesOnlyMatchingFailures()
    {
        var attempts = 0;
        var flow = Flow.Start(Step.Define<int, int>(
                "flaky",
                _ =>
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        throw new TimeoutException("transient");
                    }

                    return 7;
                }))
            .Retry(3, exception => exception is TimeoutException);
        var journal = new InMemoryJournal();

        var result = await flow.RunAsync(0, journal);

        Assert.Equal(7, result);
        Assert.Equal(2, attempts);
        Assert.Single(journal.Snapshot().OfType<StepRetryScheduled>());
    }

    [Fact]
    public async Task RethrowsNonMatchingFailuresWithoutRetryEvents()
    {
        var attempts = 0;
        var flow = Flow.Start(Step.Define<int, int>(
                "broken",
                _ =>
                {
                    attempts++;
                    throw new ArgumentException("fatal");
                }))
            .Retry(3, exception => exception is TimeoutException);
        var journal = new InMemoryJournal();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => flow.RunAsync(0, journal));

        Assert.Equal("fatal", exception.Message);
        Assert.Equal(1, attempts);
        Assert.Empty(journal.Snapshot().OfType<StepRetryScheduled>());
    }

    [Fact]
    public async Task AThrowingPredicateFailsTheRun()
    {
        var attempts = 0;
        var flow = Flow.Start(Step.Define<int, int>(
                "broken",
                _ =>
                {
                    attempts++;
                    throw new InvalidOperationException("step boom");
                }))
            .Retry(3, _ => throw new FormatException("predicate boom"));
        var journal = new InMemoryJournal();

        var exception = await Assert.ThrowsAsync<FormatException>(() => flow.RunAsync(0, journal));

        Assert.Equal("predicate boom", exception.Message);
        Assert.Equal(1, attempts);
        Assert.Empty(journal.Snapshot().OfType<StepRetryScheduled>());
    }

    [Fact]
    public async Task CancellationIsNeverRetried()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var flow = Flow.Start(Step.Define<int, int>(
                "wait",
                async (_, _, cancellationToken) =>
                {
                    attempts++;
                    entered.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return 0;
                }))
            .Retry(3);
        var journal = new InMemoryJournal();
        using var cancellation = new CancellationTokenSource();

        var run = flow.RunAsync(0, journal, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, attempts);
        Assert.Empty(journal.Snapshot().OfType<StepRetryScheduled>());
    }
}
