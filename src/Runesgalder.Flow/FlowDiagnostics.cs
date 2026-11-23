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
}
