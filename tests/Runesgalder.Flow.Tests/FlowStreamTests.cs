namespace Runesgalder.FlowTests;

public sealed class FlowStreamTests
{
    [Fact]
    public async Task BeginRunStreamsLifecycleEventsUntilCompletion()
    {
        var flow = Flow.Start(Step.Define<int, int>("inc", value => value + 1))
            .Then(Step.Define<int, string>("fmt", value => $"v{value}"));
        var journal = new InMemoryJournal();

        var run = flow.BeginRun(1, journal);
        var types = new List<Type>();
        await foreach (var runEvent in run.Events())
        {
            types.Add(runEvent.GetType());
        }

        Assert.Equal("v2", await run.Result);
        Assert.Equal(
            [
                typeof(RunStarted),
                typeof(StepStarted),
                typeof(StepCompleted),
                typeof(StepStarted),
                typeof(StepCompleted),
                typeof(RunCompleted)
            ],
            types);
        Assert.Equal(6, journal.Snapshot().Count);
    }

    [Fact]
    public async Task BeginRunEndsTheStreamWhenTheFlowFails()
    {
        var flow = Flow.Start(Step.Define<int, int>(
            "boom",
            _ => throw new InvalidOperationException("nope")));

        var run = flow.BeginRun(0);
        var events = new List<RunEvent>();
        await foreach (var runEvent in run.Events())
        {
            events.Add(runEvent);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => run.Result);
        var failed = Assert.IsType<RunFailed>(events[^1]);
        Assert.Equal(ErrorCodes.FlowStepFailed, failed.ErrorCode);
    }
}
