namespace Runesgalder.FlowTests;

public sealed class FanOutTests
{
    [Fact]
    public async Task RunsEveryBranchConcurrentlyAndKeepsDeclarationOrder()
    {
        var fastCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flow = Flow.Start(Step.Define<int, int>("seed", value => value))
            .FanOut(
                Step.Define<int, int>(
                    "slow",
                    async (_, _, cancellationToken) =>
                    {
                        await fastCompleted.Task.WaitAsync(cancellationToken);
                        return 10;
                    }),
                Step.Define<int, int>(
                    "fast",
                    (_, _, _) =>
                    {
                        fastCompleted.SetResult();
                        return ValueTask.FromResult(20);
                    }));
        var journal = new InMemoryJournal();

        var results = await flow.RunAsync(0, journal).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([10, 20], results);
        var events = journal.Snapshot();
        Assert.Equal("seed || slow | fast", Assert.IsType<RunStarted>(events[0]).FlowName);
        var completed = events.OfType<StepCompleted>().Select(step => step.StepName).Order().ToArray();
        Assert.Equal(["fast", "seed", "slow"], completed);
    }

    [Fact]
    public async Task FirstFailureCancelsSiblings()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flow = Flow.Start(Step.Define<int, int>("seed", value => value))
            .FanOut(
                Step.Define<int, int>(
                    "slow",
                    async (_, _, cancellationToken) =>
                    {
                        try
                        {
                            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled.TrySetResult();
                            throw;
                        }

                        return 0;
                    }),
                Step.Define<int, int>("failing", _ => throw new InvalidOperationException("branch boom")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => flow.RunAsync(1).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal("branch boom", exception.Message);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RethrowsTheEarliestDeclaredFailure()
    {
        var flow = Flow.Start(Step.Define<int, int>("seed", value => value))
            .FanOut(
                Step.Define<int, int>("first", _ => throw new InvalidOperationException("first boom")),
                Step.Define<int, int>("second", _ => throw new ArgumentException("second boom")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => flow.RunAsync(1).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal("first boom", exception.Message);
    }

    [Fact]
    public async Task CallerCancellationStopsEveryBranch()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flow = Flow.Start(Step.Define<int, int>("seed", value => value))
            .FanOut(
                Step.Define<int, int>(
                    "one",
                    async (_, _, cancellationToken) =>
                    {
                        entered.SetResult();
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                        return 1;
                    }),
                Step.Define<int, int>(
                    "two",
                    async (_, _, cancellationToken) =>
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                        return 2;
                    }));
        var journal = new InMemoryJournal();
        using var cancellation = new CancellationTokenSource();

        var run = flow.RunAsync(0, journal, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(ErrorCodes.FlowCancelled, Assert.IsType<RunFailed>(journal.Snapshot()[^1]).ErrorCode);
    }

    [Fact]
    public void RejectsMissingBranches()
    {
        var flow = Flow.Start(Step.Define<int, int>("seed", value => value));
        var branch = Step.Define<int, int>("branch", value => value);

        Assert.Throws<ArgumentException>(() => flow.FanOut());
        Assert.Throws<ArgumentNullException>(() => flow.FanOut(null!));
        Assert.Throws<ArgumentException>(() => flow.FanOut(branch, null!));
    }
}
