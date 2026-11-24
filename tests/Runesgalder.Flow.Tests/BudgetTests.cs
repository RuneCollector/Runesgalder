namespace Runesgalder.FlowTests;

public sealed class BudgetTests
{
    [Fact]
    public async Task WallExpiryFailsTheRunAndJournalsExceeded()
    {
        var flow = Flow.Start(Step.Define<int, int>(
                "slow",
                async (_, _, cancellationToken) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return 0;
                }))
            .Budget(new Budget { MaxWall = TimeSpan.FromMilliseconds(50) });
        var journal = new InMemoryJournal();

        var exception = await Assert.ThrowsAsync<BudgetExceededException>(() => flow.RunAsync(0, journal));

        Assert.Equal(BudgetDimension.Wall, exception.Dimension);
        var events = journal.Snapshot();
        Assert.Contains(events, runEvent => runEvent is BudgetExceeded exceeded && exceeded.Dimension == BudgetDimension.Wall);
        Assert.Contains(events, runEvent => runEvent is RunFailed failed && failed.ErrorCode == ErrorCodes.FlowBudgetExceeded);
        Assert.DoesNotContain(events, runEvent => runEvent is RunCompleted);
    }

    [Fact]
    public async Task TokenHardLimitFailsAtTheCharge()
    {
        var flow = Flow.Start(Step.Define<int, int>(
                "charge",
                async (value, context, cancellationToken) =>
                {
                    await context.ChargeTokensAsync(10, cancellationToken);
                    await context.ChargeTokensAsync(10, cancellationToken);
                    return value;
                }))
            .Budget(new Budget { MaxTokens = 15 });
        var journal = new InMemoryJournal();

        var exception = await Assert.ThrowsAsync<BudgetExceededException>(() => flow.RunAsync(0, journal));

        Assert.Equal(BudgetDimension.Tokens, exception.Dimension);
        Assert.Equal(15m, exception.Limit);
        Assert.Equal(20m, exception.Used);
        var events = journal.Snapshot();
        var exceeded = Assert.Single(events.OfType<BudgetExceeded>());
        Assert.Equal(15m, exceeded.Limit);
        Assert.Equal(20m, exceeded.Used);
        Assert.Contains(events, runEvent => runEvent is StepFailed failed && failed.ErrorCode == ErrorCodes.FlowBudgetExceeded);
        Assert.Contains(events, runEvent => runEvent is RunFailed failed && failed.ErrorCode == ErrorCodes.FlowBudgetExceeded);
    }

    [Fact]
    public async Task UsdHardLimitFailsAtTheCharge()
    {
        var flow = Flow.Start(Step.Define<int, int>(
                "charge",
                async (value, context, cancellationToken) =>
                {
                    await context.ChargeUsdAsync(0.75m, cancellationToken);
                    await context.ChargeUsdAsync(0.30m, cancellationToken);
                    return value;
                }))
            .Budget(new Budget { MaxUsd = 1.00m });

        var exception = await Assert.ThrowsAsync<BudgetExceededException>(() => flow.RunAsync(0));

        Assert.Equal(BudgetDimension.Usd, exception.Dimension);
        Assert.Equal(1.00m, exception.Limit);
        Assert.Equal(1.05m, exception.Used);
    }

    [Fact]
    public async Task WarnsOncePerDimensionPerScope()
    {
        var flow = Flow.Start(Step.Define<int, int>(
                "charge",
                async (value, context, cancellationToken) =>
                {
                    await context.ChargeTokensAsync(11, cancellationToken);
                    await context.ChargeTokensAsync(12, cancellationToken);
                    await context.ChargeTokensAsync(5, cancellationToken);
                    return value;
                }))
            .Budget(new Budget { MaxTokens = 100, WarnTokens = 10 });
        var journal = new InMemoryJournal();

        Assert.Equal(7, await flow.RunAsync(7, journal));

        var warning = Assert.Single(journal.Snapshot().OfType<BudgetWarning>());
        Assert.Equal(BudgetDimension.Tokens, warning.Dimension);
        Assert.Equal(10m, warning.Limit);
        Assert.Equal(11m, warning.Used);
    }

    [Fact]
    public async Task WallWarnFiresWithoutFailingTheRun()
    {
        var flow = Flow.Start(Step.Define<int, int>(
                "work",
                async (value, _, cancellationToken) =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
                    return value;
                }))
            .Budget(new Budget { MaxWall = TimeSpan.FromSeconds(30), WarnWall = TimeSpan.FromMilliseconds(20) });
        var journal = new InMemoryJournal();

        Assert.Equal(3, await flow.RunAsync(3, journal));

        var warning = Assert.Single(journal.Snapshot().OfType<BudgetWarning>());
        Assert.Equal(BudgetDimension.Wall, warning.Dimension);
        Assert.Equal(0.02m, warning.Limit);
        Assert.Contains(journal.Snapshot(), runEvent => runEvent is RunCompleted);
    }

    [Fact]
    public async Task ChargesWithoutABudgetAreNoOps()
    {
        var flow = Flow.Start(Step.Define<int, int>(
            "charge",
            async (value, context, cancellationToken) =>
            {
                await context.ChargeTokensAsync(1_000_000, cancellationToken);
                await context.ChargeUsdAsync(1000m, cancellationToken);
                return value;
            }));
        var journal = new InMemoryJournal();

        Assert.Equal(1, await flow.RunAsync(1, journal));

        var events = journal.Snapshot();
        Assert.DoesNotContain(events, runEvent => runEvent is BudgetWarning or BudgetExceeded);
        Assert.Contains(events, runEvent => runEvent is RunCompleted);
    }

    [Fact]
    public async Task NestedScopesChargeEveryEnclosingBudget()
    {
        var flow = Flow.Start(Step.Define<int, int>(
                "charge",
                async (value, context, cancellationToken) =>
                {
                    await context.ChargeTokensAsync(11, cancellationToken);
                    return value;
                }))
            .Budget(new Budget { MaxTokens = 100 })
            .Budget(new Budget { MaxTokens = 10 });

        var exception = await Assert.ThrowsAsync<BudgetExceededException>(() => flow.RunAsync(0));

        Assert.Equal(10m, exception.Limit);
    }

    [Fact]
    public async Task InnerScopeEndsWhenItsSegmentReturns()
    {
        var flow = Flow.Start(Step.Define<int, int>(
                "first",
                async (value, context, cancellationToken) =>
                {
                    await context.ChargeTokensAsync(6, cancellationToken);
                    return value;
                }))
            .Budget(new Budget { MaxTokens = 10 })
            .Then(Step.Define<int, int>(
                "second",
                async (value, context, cancellationToken) =>
                {
                    await context.ChargeTokensAsync(6, cancellationToken);
                    return value;
                }))
            .Budget(new Budget { MaxTokens = 100 });

        Assert.Equal(4, await flow.RunAsync(4));
    }

    [Fact]
    public async Task CallerCancellationWinsOverTheWallBudget()
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
            .Budget(new Budget { MaxWall = TimeSpan.FromSeconds(30) });
        var journal = new InMemoryJournal();
        using var cancellation = new CancellationTokenSource();

        var run = flow.RunAsync(0, journal, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        var events = journal.Snapshot();
        Assert.Contains(events, runEvent => runEvent is RunFailed failed && failed.ErrorCode == ErrorCodes.FlowCancelled);
        Assert.DoesNotContain(events, runEvent => runEvent is BudgetExceeded);
    }

    [Fact]
    public void RejectsInvalidBudgets()
    {
        var flow = Flow.Start(Step.Define<int, int>("seed", value => value));

        Assert.Throws<ArgumentNullException>(() => flow.Budget(null!));
        Assert.Throws<ArgumentException>(() => flow.Budget(new Budget()));
        Assert.Throws<ArgumentException>(() => flow.Budget(new Budget { MaxTokens = 0 }));
        Assert.Throws<ArgumentException>(() => flow.Budget(new Budget { MaxUsd = -1m }));
        Assert.Throws<ArgumentException>(() => flow.Budget(new Budget { MaxWall = TimeSpan.Zero }));
        Assert.Throws<ArgumentException>(() => flow.Budget(new Budget { MaxWall = Timeout.InfiniteTimeSpan }));
        Assert.Throws<ArgumentException>(() => flow.Budget(new Budget { MaxTokens = 10, WarnTokens = 10 }));
        Assert.Throws<ArgumentException>(() => flow.Budget(new Budget { MaxTokens = 10, WarnTokens = -1 }));
        Assert.Throws<ArgumentException>(() => flow.Budget(new Budget { MaxUsd = 1m, WarnUsd = 2m }));
        Assert.Throws<ArgumentException>(() => flow.Budget(new Budget { MaxWall = TimeSpan.FromSeconds(5), WarnWall = TimeSpan.Zero }));
    }

    [Fact]
    public async Task RejectsNegativeCharges()
    {
        var context = new FlowContext(RunId.New(), new InMemoryJournal());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await context.ChargeTokensAsync(-1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await context.ChargeUsdAsync(-0.01m));
    }
}
