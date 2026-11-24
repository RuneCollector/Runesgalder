namespace Runesgalder.FlowTests;

public sealed class TimeoutTests
{
    [Fact]
    public async Task FlowDeadlineThrowsAndJournalsTimedOut()
    {
        var flow = Flow.Start(Step.Define<int, int>(
                "slow",
                async (_, _, cancellationToken) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return 0;
                }))
            .Timeout(TimeSpan.FromMilliseconds(50));
        var journal = new InMemoryJournal();

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => flow.RunAsync(0, journal));

        Assert.Contains("slow", exception.Message);
        var events = journal.Snapshot();
        Assert.Contains(events, runEvent => runEvent is RunFailed failed && failed.ErrorCode == ErrorCodes.FlowTimedOut);
        Assert.DoesNotContain(events, runEvent => runEvent is RunCompleted);
    }

    [Fact]
    public async Task StepDeadlineJournalsTimedOutWithOneStepEventPair()
    {
        var flow = Flow.Start(Step.Define<int, int>(
                "slow",
                async (_, _, cancellationToken) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return 0;
                })
            .Timeout(TimeSpan.FromMilliseconds(50)));
        var journal = new InMemoryJournal();

        await Assert.ThrowsAsync<TimeoutException>(() => flow.RunAsync(0, journal));

        var events = journal.Snapshot();
        Assert.Single(events.OfType<StepStarted>());
        Assert.Contains(events, runEvent => runEvent is StepFailed failed && failed.ErrorCode == ErrorCodes.FlowTimedOut);
        Assert.Contains(events, runEvent => runEvent is RunFailed failed && failed.ErrorCode == ErrorCodes.FlowTimedOut);
    }

    [Fact]
    public async Task CallerCancellationWinsOverTheFlowDeadline()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flow = Flow.Start(Step.Define<int, int>(
                "wait",
                async (_, _, cancellationToken) =>
                {
                    entered.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return 0;
                }))
            .Timeout(TimeSpan.FromSeconds(30));
        var journal = new InMemoryJournal();
        using var cancellation = new CancellationTokenSource();

        var run = flow.RunAsync(0, journal, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Contains(journal.Snapshot(), runEvent => runEvent is RunFailed failed && failed.ErrorCode == ErrorCodes.FlowCancelled);
    }

    [Fact]
    public async Task CallerCancellationWinsOverTheStepDeadline()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flow = Flow.Start(Step.Define<int, int>(
                "wait",
                async (_, _, cancellationToken) =>
                {
                    entered.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return 0;
                })
            .Timeout(TimeSpan.FromSeconds(30)));
        var journal = new InMemoryJournal();
        using var cancellation = new CancellationTokenSource();

        var run = flow.RunAsync(0, journal, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Contains(journal.Snapshot(), runEvent => runEvent is StepFailed failed && failed.ErrorCode == ErrorCodes.FlowCancelled);
        Assert.Contains(journal.Snapshot(), runEvent => runEvent is RunFailed failed && failed.ErrorCode == ErrorCodes.FlowCancelled);
    }

    [Fact]
    public void RejectsNonPositiveAndInfiniteDeadlines()
    {
        var flow = Flow.Start(Step.Define<int, int>("seed", value => value));
        var step = Step.Define<int, int>("seed", value => value);

        Assert.Throws<ArgumentOutOfRangeException>(() => flow.Timeout(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => flow.Timeout(TimeSpan.FromMilliseconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => flow.Timeout(Timeout.InfiniteTimeSpan));
        Assert.Throws<ArgumentOutOfRangeException>(() => step.Timeout(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => step.Timeout(Timeout.InfiniteTimeSpan));
    }
}
