namespace Runesgalder.FlowTests;

public sealed class FlowCompositionTests
{
    [Fact]
    public async Task ThenComposesTypedStepsAndEmitsLifecycleEvents()
    {
        var flow = Flow.Start(Step.Define<int, int>("add-one", value => value + 1))
            .Then(Step.Define<int, string>("format", value => $"value:{value}"));
        var journal = new InMemoryJournal();

        var result = await flow.RunAsync(41, journal);

        Assert.Equal("value:42", result);
        var events = journal.Snapshot();
        Assert.Collection(
            events,
            runEvent => Assert.IsType<RunStarted>(runEvent),
            runEvent => Assert.IsType<StepStarted>(runEvent),
            runEvent => Assert.IsType<StepCompleted>(runEvent),
            runEvent => Assert.IsType<StepStarted>(runEvent),
            runEvent => Assert.IsType<StepCompleted>(runEvent),
            runEvent => Assert.IsType<RunCompleted>(runEvent));

        var runStarted = Assert.IsType<RunStarted>(events[0]);
        Assert.Equal("add-one -> format", runStarted.FlowName);

        var steps = events.OfType<StepCompleted>().Select(step => step.StepName).ToArray();
        Assert.Equal(["add-one", "format"], steps);
    }

    [Fact]
    public async Task RetryRepeatsTheFlowAndEmitsRetryEvents()
    {
        var attempts = 0;
        var flow = Flow.Start(Step.Define<int, int>(
                "flaky",
                _ =>
                {
                    attempts++;
                    if (attempts < 3)
                    {
                        throw new InvalidOperationException("transient");
                    }

                    return 7;
                }))
            .Retry(3);
        var journal = new InMemoryJournal();

        var result = await flow.RunAsync(0, journal);

        Assert.Equal(7, result);
        Assert.Equal(3, attempts);
        var events = journal.Snapshot();
        Assert.Equal(2, events.OfType<StepRetryScheduled>().Count());
        Assert.Equal(3, events.OfType<StepStarted>().Count());
        Assert.Equal(2, events.OfType<StepFailed>().Count());
        Assert.Single(events.OfType<RunCompleted>());
        Assert.Empty(events.OfType<RunFailed>());
    }

    [Fact]
    public async Task CancellationEmitsCancelledEventsAndRethrows()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flow = Flow.Start(Step.Define<int, int>(
            "wait",
            async (_, _, cancellationToken) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            }));
        var journal = new InMemoryJournal();
        using var cancellation = new CancellationTokenSource();

        var run = flow.RunAsync(0, journal, cancellation.Token);
        await entered.Task;
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        var events = journal.Snapshot();
        Assert.Contains(events, runEvent => runEvent is StepFailed failed && failed.ErrorCode == ErrorCodes.FlowCancelled);
        Assert.Contains(events, runEvent => runEvent is RunFailed failed && failed.ErrorCode == ErrorCodes.FlowCancelled);
    }
}
