using System.Diagnostics;

namespace Runesgalder;

/// <summary>OpenTelemetry-compatible tracing for flows.</summary>
public static class FlowDiagnostics
{
    /// <summary>The shared activity source; register its name with an OTel tracer provider to export spans.</summary>
    public static ActivitySource ActivitySource { get; } = new("Runesgalder.Flow", "0.1.0");

    /// <summary>The run identifier attribute.</summary>
    public const string RunIdTag = "run.id";

    /// <summary>The step identifier attribute.</summary>
    public const string StepIdTag = "step.id";

    /// <summary>The flow name attribute.</summary>
    public const string FlowNameTag = "flow.name";

    /// <summary>The model name attribute.</summary>
    public const string ModelNameTag = "model.name";

    /// <summary>The input token count attribute.</summary>
    public const string TokenInputTag = "token.input";

    /// <summary>The output token count attribute.</summary>
    public const string TokenOutputTag = "token.output";

    /// <summary>The run outcome attribute; <c>ok</c> or a stable error code.</summary>
    public const string RunOutcomeTag = "flow.outcome";

    /// <summary>The step name attribute; the human-readable step label.</summary>
    public const string StepNameTag = "step.name";

    /// <summary>The budget dimension attribute; <c>Tokens</c>, <c>Usd</c>, or <c>Wall</c>.</summary>
    public const string BudgetDimensionTag = "budget.dimension";

    /// <summary>The hard budget limit attribute.</summary>
    public const string BudgetLimitTag = "budget.limit";

    /// <summary>The usage that crossed the hard budget limit attribute.</summary>
    public const string BudgetUsedTag = "budget.used";

    /// <summary>The approval identifier attribute.</summary>
    public const string ApprovalIdTag = "approval.id";

    /// <summary>The approval decision attribute.</summary>
    public const string ApprovalDecisionTag = "approval.decision";

    /// <summary>The cost attribute in US dollars; set by host model adapters.</summary>
    public const string CostUsdTag = "cost.usd";
}
