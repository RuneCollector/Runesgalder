using System.Diagnostics.Metrics;

namespace Runesgalder.FlowTests;

public sealed class FlowMetricsTests
{
    [Fact]
    public async Task CountsASuccessfulRunAndStep()
    {
        using var metrics = new MetricCapture();
        var flow = Flow.Start(Step.Define<int, int>("metrics-ok-flow", value => value + 1));

        var output = await flow.RunAsync(1);

        Assert.Equal(2, output);
        var run = Assert.Single(metrics.For("flow.runs", "metrics-ok-flow"));
        Assert.Equal(1, run.Value);
        Assert.Equal("ok", run.Tags[FlowDiagnostics.RunOutcomeTag]);
        var step = Assert.Single(metrics.For("flow.steps", "metrics-ok-flow"));
        Assert.Equal("ok", step.Tags["step.outcome"]);
        Assert.Equal("metrics-ok-flow", step.Tags[FlowDiagnostics.StepNameTag]);
        var duration = Assert.Single(metrics.For("flow.step.duration", "metrics-ok-flow"));
        Assert.True(duration.Value > 0);
    }

    [Fact]
    public async Task CountsAFailedRunAndStep()
    {
        using var metrics = new MetricCapture();
        var flow = Flow.Start(Step.Define<int, int>(
            "metrics-fail-flow",
            _ => throw new InvalidOperationException("boom")));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await flow.RunAsync(1));

        var run = Assert.Single(metrics.For("flow.runs", "metrics-fail-flow"));
        Assert.Equal(ErrorCodes.FlowStepFailed, run.Tags[FlowDiagnostics.RunOutcomeTag]);
        var step = Assert.Single(metrics.For("flow.steps", "metrics-fail-flow"));
        Assert.Equal(ErrorCodes.FlowStepFailed, step.Tags["step.outcome"]);
    }

    [Fact]
    public async Task CountsChargesWithoutABudget()
    {
        using var metrics = new MetricCapture();
        var flow = Flow.Start(Step.Define<int, int>(
            "metrics-charge-flow",
            async (value, context, cancellationToken) =>
            {
                await context.ChargeTokensAsync(7, cancellationToken);
                await context.ChargeUsdAsync(0.25m, cancellationToken);
                return value;
            }));

        await flow.RunAsync(1);

        var tokens = Assert.Single(metrics.For("flow.tokens", "metrics-charge-flow"));
        Assert.Equal(7, tokens.Value);
        var cost = Assert.Single(metrics.For("flow.cost.usd", "metrics-charge-flow"));
        Assert.Equal(0.25, cost.Value, 6);
    }

    [Fact]
    public async Task CountsBudgetWarningsAndExceeded()
    {
        using var metrics = new MetricCapture();
        var flow = Flow.Start(Step.Define<int, int>(
                "metrics-budget-flow",
                async (value, context, cancellationToken) =>
                {
                    await context.ChargeTokensAsync(11, cancellationToken);
                    await context.ChargeTokensAsync(100, cancellationToken);
                    return value;
                }))
            .Budget(new Budget { MaxTokens = 100, WarnTokens = 10 });

        await Assert.ThrowsAsync<BudgetExceededException>(async () => await flow.RunAsync(1));

        var warning = Assert.Single(metrics.For("flow.budget.warnings", "metrics-budget-flow"));
        Assert.Equal(nameof(BudgetDimension.Tokens), warning.Tags[FlowDiagnostics.BudgetDimensionTag]);
        var exceeded = Assert.Single(metrics.For("flow.budget.exceeded", "metrics-budget-flow"));
        Assert.Equal(nameof(BudgetDimension.Tokens), exceeded.Tags[FlowDiagnostics.BudgetDimensionTag]);
        var run = Assert.Single(metrics.For("flow.runs", "metrics-budget-flow"));
        Assert.Equal(ErrorCodes.FlowBudgetExceeded, run.Tags[FlowDiagnostics.RunOutcomeTag]);
    }

    [Fact]
    public async Task CountsApprovalDecisions()
    {
        using var metrics = new MetricCapture();
        var approved = Flow.Start(Step.Define<int, int>("metrics-approval-ok", value => value))
            .RequireApproval(
                new FixedApprover(ApprovalDecision.Approved),
                "flow.run",
                "Approve the run",
                TimeSpan.FromSeconds(5));
        var denied = Flow.Start(Step.Define<int, int>("metrics-approval-denied", value => value))
            .RequireApproval(
                new FixedApprover(ApprovalDecision.Denied),
                "flow.run",
                "Approve the run",
                TimeSpan.FromSeconds(5));

        await approved.RunAsync(1);
        await Assert.ThrowsAsync<ApprovalDeniedException>(async () => await denied.RunAsync(1));

        var approvedMetric = Assert.Single(metrics.For("flow.approvals", "metrics-approval-ok"));
        Assert.Equal(
            nameof(ApprovalDecision.Approved),
            approvedMetric.Tags[FlowDiagnostics.ApprovalDecisionTag]);
        var deniedMetric = Assert.Single(metrics.For("flow.approvals", "metrics-approval-denied"));
        Assert.Equal(
            nameof(ApprovalDecision.Denied),
            deniedMetric.Tags[FlowDiagnostics.ApprovalDecisionTag]);
    }

    private sealed class FixedApprover(ApprovalDecision decision) : IApprover
    {
        public ValueTask<ApprovalDecision> RequestAsync(
            ApprovalRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(decision);
    }

    private sealed record Measurement(
        string Instrument,
        double Value,
        IReadOnlyDictionary<string, object?> Tags);

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly List<Measurement> _measurements = [];
        private readonly object _gate = new();

        public MetricCapture()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == "Runesgalder.Flow")
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                Add(instrument, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                Add(instrument, value, tags));
            _listener.Start();
        }

        public Measurement[] For(string instrument, string flowName)
        {
            lock (_gate)
            {
                return _measurements
                    .Where(item => item.Instrument == instrument
                        && Equals(item.Tags[FlowDiagnostics.FlowNameTag], flowName))
                    .ToArray();
            }
        }

        public void Dispose() => _listener.Dispose();

        private void Add(
            Instrument instrument,
            double value,
            ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                dictionary[tag.Key] = tag.Value;
            }

            lock (_gate)
            {
                _measurements.Add(new Measurement(instrument.Name, value, dictionary));
            }
        }
    }
}
