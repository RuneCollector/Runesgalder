using System.Diagnostics;
using System.Globalization;

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

    [Fact]
    public async Task SpansRecordTheRunOutcome()
    {
        var spans = new List<Activity>();
        using var listener = ListenTo(spans);
        var flow = Flow.Start(Step.Define<int, int>("outcome-step", value => value + 1));

        var run = flow.BeginRun(1);
        await run.Result;
        var owned = Owned(spans, run.RunId);

        var step = Assert.Single(owned, span => span.OperationName == "outcome-step");
        Assert.Equal("outcome-step", step.GetTagItem(FlowDiagnostics.StepNameTag));
        var root = Assert.Single(owned, span => span.OperationName == "flow outcome-step");
        Assert.Equal("ok", root.GetTagItem(FlowDiagnostics.RunOutcomeTag));
    }

    [Fact]
    public async Task SpansRecordBudgetFailureAttributes()
    {
        var spans = new List<Activity>();
        using var listener = ListenTo(spans);
        var flow = Flow.Start(Step.Define<int, int>(
                "overrun",
                async (value, context, cancellationToken) =>
                {
                    await context.ChargeTokensAsync(2, cancellationToken);
                    return value;
                }))
            .Budget(new Budget { MaxTokens = 1 });

        var run = flow.BeginRun(1);
        await Assert.ThrowsAsync<BudgetExceededException>(async () => await run.Result);
        var owned = Owned(spans, run.RunId);

        var step = Assert.Single(owned, span => span.OperationName == "overrun");
        Assert.Equal("Tokens", step.GetTagItem(FlowDiagnostics.BudgetDimensionTag));
        Assert.Equal(1, TagDouble(step, FlowDiagnostics.BudgetLimitTag));
        Assert.Equal(2, TagDouble(step, FlowDiagnostics.BudgetUsedTag));
        var root = Assert.Single(owned, span => span.OperationName == "flow overrun");
        Assert.Equal(ErrorCodes.FlowBudgetExceeded, root.GetTagItem(FlowDiagnostics.RunOutcomeTag));
        Assert.Equal("Tokens", root.GetTagItem(FlowDiagnostics.BudgetDimensionTag));
        Assert.Equal(1, TagDouble(root, FlowDiagnostics.BudgetLimitTag));
        Assert.Equal(2, TagDouble(root, FlowDiagnostics.BudgetUsedTag));
    }

    [Fact]
    public async Task SpansRecordApprovalDenialAttributes()
    {
        var spans = new List<Activity>();
        using var listener = ListenTo(spans);
        var flow = Flow.Start(Step.Define<int, int>("guarded", value => value))
            .RequireApproval(
                new DenyingApprover(),
                "flow.run",
                "Approve the run",
                TimeSpan.FromSeconds(5));

        var run = flow.BeginRun(1);
        var denied = await Assert.ThrowsAsync<ApprovalDeniedException>(async () => await run.Result);
        var owned = Owned(spans, run.RunId);

        var root = Assert.Single(owned, span => span.OperationName == "flow guarded");
        Assert.Equal(ErrorCodes.FlowApprovalDenied, root.GetTagItem(FlowDiagnostics.RunOutcomeTag));
        Assert.Equal(denied.ApprovalId.ToString(), root.GetTagItem(FlowDiagnostics.ApprovalIdTag));
        Assert.Equal(nameof(ApprovalDecision.Denied), root.GetTagItem(FlowDiagnostics.ApprovalDecisionTag));
    }

    [Fact]
    public async Task SpansDoNotRecordPayloads()
    {
        var spans = new List<Activity>();
        using var listener = ListenTo(spans);
        const string input = "payload-prompt-9f3";
        const string output = "payload-completion-9f3";
        var flow = Flow.Start(Step.Define<string, string>("echo", _ => output));

        var run = flow.BeginRun(input);
        await run.Result;

        lock (spans)
        {
            Assert.NotEmpty(spans);
            foreach (var activity in spans)
            {
                foreach (var tag in activity.TagObjects)
                {
                    var value = Convert.ToString(tag.Value, CultureInfo.InvariantCulture);
                    Assert.DoesNotContain(input, value, StringComparison.Ordinal);
                    Assert.DoesNotContain(output, value, StringComparison.Ordinal);
                }
            }
        }
    }

    private static double TagDouble(Activity activity, string tag) =>
        activity.GetTagItem(tag) is { } value
            ? Convert.ToDouble(value, CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"The span has no '{tag}' tag.");

    private sealed class DenyingApprover : IApprover
    {
        public ValueTask<ApprovalDecision> RequestAsync(
            ApprovalRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApprovalDecision.Denied);
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
