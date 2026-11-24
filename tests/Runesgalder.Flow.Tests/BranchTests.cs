namespace Runesgalder.FlowTests;

public sealed class BranchTests
{
    [Fact]
    public async Task RunsOnlyTheTrueBranch()
    {
        var trueRuns = 0;
        var falseRuns = 0;
        var flow = Flow.Start(Step.Define<int, int>("seed", value => value))
            .Branch(
                value => value > 0,
                Step.Define<int, string>(
                    "positive",
                    value =>
                    {
                        trueRuns++;
                        return $"positive:{value}";
                    }),
                Step.Define<int, string>(
                    "non-positive",
                    value =>
                    {
                        falseRuns++;
                        return $"non-positive:{value}";
                    }));
        var journal = new InMemoryJournal();

        var result = await flow.RunAsync(5, journal);

        Assert.Equal("positive:5", result);
        Assert.Equal(1, trueRuns);
        Assert.Equal(0, falseRuns);
        var events = journal.Snapshot();
        Assert.Equal("seed ? positive : non-positive", Assert.IsType<RunStarted>(events[0]).FlowName);
        Assert.Equal(["seed", "positive"], events.OfType<StepCompleted>().Select(step => step.StepName).ToArray());
    }

    [Fact]
    public async Task RunsOnlyTheFalseBranch()
    {
        var trueRuns = 0;
        var falseRuns = 0;
        var flow = Flow.Start(Step.Define<int, int>("seed", value => value))
            .Branch(
                value => value > 0,
                Step.Define<int, int>(
                    "positive",
                    value =>
                    {
                        trueRuns++;
                        return value;
                    }),
                Step.Define<int, int>(
                    "non-positive",
                    value =>
                    {
                        falseRuns++;
                        return -value;
                    }));
        var journal = new InMemoryJournal();

        var result = await flow.RunAsync(-3, journal);

        Assert.Equal(3, result);
        Assert.Equal(0, trueRuns);
        Assert.Equal(1, falseRuns);
        Assert.Equal(["seed", "non-positive"], journal.Snapshot().OfType<StepCompleted>().Select(step => step.StepName).ToArray());
    }

    [Fact]
    public async Task PredicateFailureFailsTheRunWithoutRunningABranch()
    {
        var branchRuns = 0;
        var flow = Flow.Start(Step.Define<int, int>("seed", value => value))
            .Branch(
                _ => throw new InvalidOperationException("predicate boom"),
                Step.Define<int, int>(
                    "left",
                    value =>
                    {
                        branchRuns++;
                        return value;
                    }),
                Step.Define<int, int>(
                    "right",
                    value =>
                    {
                        branchRuns++;
                        return value;
                    }));
        var journal = new InMemoryJournal();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => flow.RunAsync(1, journal));

        Assert.Equal("predicate boom", exception.Message);
        Assert.Equal(0, branchRuns);
        Assert.Equal(ErrorCodes.FlowStepFailed, Assert.IsType<RunFailed>(journal.Snapshot()[^1]).ErrorCode);
    }

    [Fact]
    public async Task CancellationBeforeThePredicateStopsTheRun()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var branchRuns = 0;
        var flow = Flow.Start(Step.Define<int, int>(
                "wait",
                async (_, _, cancellationToken) =>
                {
                    entered.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return 0;
                }))
            .Branch(
                _ => true,
                Step.Define<int, int>(
                    "left",
                    value =>
                    {
                        branchRuns++;
                        return value;
                    }),
                Step.Define<int, int>(
                    "right",
                    value =>
                    {
                        branchRuns++;
                        return value;
                    }));
        var journal = new InMemoryJournal();
        using var cancellation = new CancellationTokenSource();

        var run = flow.RunAsync(0, journal, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(0, branchRuns);
    }
}
