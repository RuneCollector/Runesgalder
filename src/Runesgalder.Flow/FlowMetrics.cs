using System.Diagnostics.Metrics;

namespace Runesgalder;

/// <summary>Emits the Flow metrics; a no-op unless a host registers the meter.</summary>
internal static class FlowMetrics
{
    public const string OkOutcome = "ok";

    private const string StepOutcomeTag = "step.outcome";

    private static readonly Meter Meter = new("Runesgalder.Flow", "0.1.0");

    private static readonly Counter<long> Runs = Meter.CreateCounter<long>(
        "flow.runs",
        "{run}",
        "Runs that ended.");

    private static readonly Counter<long> Steps = Meter.CreateCounter<long>(
        "flow.steps",
        "{step}",
        "Steps that ended.");

    private static readonly Histogram<double> StepDuration = Meter.CreateHistogram<double>(
        "flow.step.duration",
        "s",
        "Step duration in seconds.");

    private static readonly Counter<long> Tokens = Meter.CreateCounter<long>(
        "flow.tokens",
        "{token}",
        "Tokens charged to a run.");

    private static readonly Counter<double> CostUsd = Meter.CreateCounter<double>(
        "flow.cost.usd",
        "USD",
        "US dollars charged to a run.");

    private static readonly Counter<long> BudgetWarnings = Meter.CreateCounter<long>(
        "flow.budget.warnings",
        "{warning}",
        "Budget warn thresholds crossed.");

    private static readonly Counter<long> BudgetExceeded = Meter.CreateCounter<long>(
        "flow.budget.exceeded",
        "{exceeded}",
        "Hard budget limits crossed.");

    private static readonly Counter<long> Approvals = Meter.CreateCounter<long>(
        "flow.approvals",
        "{decision}",
        "Approval decisions reached.");

    public static void RecordRun(string flowName, string outcome) =>
        Runs.Add(
            1,
            new KeyValuePair<string, object?>(FlowDiagnostics.FlowNameTag, flowName),
            new KeyValuePair<string, object?>(FlowDiagnostics.RunOutcomeTag, outcome));

    public static void RecordStep(string flowName, string stepName, string outcome, TimeSpan duration)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new(FlowDiagnostics.FlowNameTag, flowName),
            new(FlowDiagnostics.StepNameTag, stepName),
            new(StepOutcomeTag, outcome),
        };
        Steps.Add(1, tags);
        StepDuration.Record(duration.TotalSeconds, tags);
    }

    public static void RecordTokens(string flowName, long tokens) =>
        Tokens.Add(
            tokens,
            new KeyValuePair<string, object?>(FlowDiagnostics.FlowNameTag, flowName));

    public static void RecordUsd(string flowName, decimal usd) =>
        CostUsd.Add(
            (double)usd,
            new KeyValuePair<string, object?>(FlowDiagnostics.FlowNameTag, flowName));

    public static void RecordBudgetWarning(string flowName, BudgetDimension dimension) =>
        BudgetWarnings.Add(1, BudgetTags(flowName, dimension));

    public static void RecordBudgetExceeded(string flowName, BudgetDimension dimension) =>
        BudgetExceeded.Add(1, BudgetTags(flowName, dimension));

    public static void RecordApproval(string flowName, ApprovalDecision decision) =>
        Approvals.Add(
            1,
            new KeyValuePair<string, object?>(FlowDiagnostics.FlowNameTag, flowName),
            new KeyValuePair<string, object?>(FlowDiagnostics.ApprovalDecisionTag, decision.ToString()));

    private static KeyValuePair<string, object?>[] BudgetTags(string flowName, BudgetDimension dimension) =>
    [
        new(FlowDiagnostics.FlowNameTag, flowName),
        new(FlowDiagnostics.BudgetDimensionTag, dimension.ToString()),
    ];
}
