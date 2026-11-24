namespace Runesgalder.FlowTests;

public sealed class FallbackTests
{
    [Fact]
    public async Task RecoversWithTheAlternativeFromTheOriginalInput()
    {
        var alternativeInput = 0;
        var flow = Flow.Start(Step.Define<int, int>("primary", _ => throw new InvalidOperationException("primary boom")))
            .Fallback(Step.Define<int, int>(
                "alternative",
                value =>
                {
                    alternativeInput = value;
                    return value * 2;
                }));
        var journal = new InMemoryJournal();

        var result = await flow.RunAsync(21, journal);

        Assert.Equal(42, result);
        Assert.Equal(21, alternativeInput);
        var events = journal.Snapshot();
        Assert.Contains(events, runEvent => runEvent is StepFailed failed && failed.StepName == "primary");
        Assert.Contains(events, runEvent => runEvent is StepCompleted completed && completed.StepName == "alternative");
        Assert.Single(events.OfType<RunCompleted>());
    }

    [Fact]
    public async Task SkipsTheFallbackWhenTheFilterRejectsTheFailure()
    {
        var alternativeRuns = 0;
        var flow = Flow.Start(Step.Define<int, int>("primary", _ => throw new InvalidOperationException("primary boom")))
            .Fallback(
                Step.Define<int, int>(
                    "alternative",
                    value =>
                    {
                        alternativeRuns++;
                        return value;
                    }),
                when: exception => exception is not InvalidOperationException);
        var journal = new InMemoryJournal();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => flow.RunAsync(1, journal));

        Assert.Equal("primary boom", exception.Message);
        Assert.Equal(0, alternativeRuns);
        Assert.Equal(ErrorCodes.FlowStepFailed, Assert.IsType<RunFailed>(journal.Snapshot()[^1]).ErrorCode);
    }

    [Fact]
    public async Task CallerCancellationDoesNotTriggerTheFallback()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var alternativeRuns = 0;
        var flow = Flow.Start(Step.Define<int, int>(
                "wait",
                async (_, _, cancellationToken) =>
                {
                    entered.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return 0;
                }))
            .Fallback(Step.Define<int, int>(
                "alternative",
                value =>
                {
                    alternativeRuns++;
                    return value;
                }));
        var journal = new InMemoryJournal();
        using var cancellation = new CancellationTokenSource();

        var run = flow.RunAsync(0, journal, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(0, alternativeRuns);
    }

    [Fact]
    public async Task AggregatesBothFailuresWithThePrimaryFirst()
    {
        var flow = Flow.Start(Step.Define<int, int>("primary", _ => throw new InvalidOperationException("primary boom")))
            .Fallback(Step.Define<int, int>("alternative", _ => throw new ArgumentException("alternative boom")));
        var journal = new InMemoryJournal();

        var exception = await Assert.ThrowsAsync<AggregateException>(() => flow.RunAsync(1, journal));

        Assert.Collection(
            exception.InnerExceptions,
            inner => Assert.Equal("primary boom", Assert.IsType<InvalidOperationException>(inner).Message),
            inner => Assert.Equal("alternative boom", Assert.IsType<ArgumentException>(inner).Message));
    }

    [Fact]
    public async Task AThrowingFilterFailsTheRun()
    {
        var alternativeRuns = 0;
        var flow = Flow.Start(Step.Define<int, int>("primary", _ => throw new InvalidOperationException("primary boom")))
            .Fallback(
                Step.Define<int, int>(
                    "alternative",
                    value =>
                    {
                        alternativeRuns++;
                        return value;
                    }),
                when: _ => throw new FormatException("filter boom"));
        var journal = new InMemoryJournal();

        var exception = await Assert.ThrowsAsync<FormatException>(() => flow.RunAsync(1, journal));

        Assert.Equal("filter boom", exception.Message);
        Assert.Equal(0, alternativeRuns);
        Assert.Equal(ErrorCodes.FlowStepFailed, Assert.IsType<RunFailed>(journal.Snapshot()[^1]).ErrorCode);
    }
}
