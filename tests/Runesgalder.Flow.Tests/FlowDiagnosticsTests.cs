using System.Diagnostics;

namespace Runesgalder.FlowTests;

public sealed class FlowDiagnosticsTests
{
    [Fact]
    public async Task SpansRecordAStepFailure()
    {
        var spans = new List<Activity>();
        using var listener = ListenTo(spans);
        var flow = Flow.Start(Step.Define<int, int>(
            "explode",
            _ => throw new InvalidOperationException("boom")));

        var run = flow.BeginRun(1);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await run.Result);
        var owned = Owned(spans, run.RunId);

        var step = Assert.Single(owned, span => span.OperationName == "explode");
        Assert.Equal(ActivityStatusCode.Error, step.Status);
        Assert.Equal("boom", step.StatusDescription);
        var root = Assert.Single(owned, span => span.OperationName == "flow explode");
        Assert.Equal(ActivityStatusCode.Error, root.Status);
        Assert.Equal("boom", root.StatusDescription);
    }

    [Fact]
    public async Task SpansRecordCancellation()
    {
        var spans = new List<Activity>();
        using var listener = ListenTo(spans);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var flow = Flow.Start(Step.Define<int, int>(
            "slow",
            async (_, _, cancellationToken) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            }));

        var run = flow.BeginRun(0, journal: null, cancellationToken: cancellation.Token);
        await entered.Task;
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await run.Result);
        var owned = Owned(spans, run.RunId);

        var step = Assert.Single(owned, span => span.OperationName == "slow");
        Assert.Equal(ActivityStatusCode.Error, step.Status);
        Assert.Equal("cancelled", step.StatusDescription);
        var root = Assert.Single(owned, span => span.OperationName == "flow slow");
        Assert.Equal(ActivityStatusCode.Error, root.Status);
        Assert.Equal("cancelled", root.StatusDescription);
    }

    private static Activity[] Owned(List<Activity> spans, RunId runId)
    {
        lock (spans)
        {
            return spans.Where(span => Equals(span.GetTagItem(FlowDiagnostics.RunIdTag), runId.ToString())).ToArray();
        }
    }

    private static ActivityListener ListenTo(List<Activity> spans)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Runesgalder.Flow",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (spans)
                {
                    spans.Add(activity);
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
