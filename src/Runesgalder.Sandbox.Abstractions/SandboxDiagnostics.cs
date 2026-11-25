using System.Diagnostics;

namespace Runesgalder.Sandbox;

/// <summary>OpenTelemetry-compatible tracing for the sandbox; a no-op unless a listener is registered.</summary>
public static class SandboxDiagnostics
{
    /// <summary>The shared activity source; register its name with an OTel tracer provider to export spans.</summary>
    public static ActivitySource ActivitySource { get; } = new("Runesgalder.Sandbox", "0.1.0");

    /// <summary>The run identifier attribute; the same value the MCP envelope carries.</summary>
    public const string RunIdTag = "run.id";

    /// <summary>The sandbox backend attribute; <c>container</c> in v0.1.</summary>
    public const string SandboxBackendTag = "sandbox.backend";

    /// <summary>The policy decision attribute; <c>allow</c> or <c>deny</c>.</summary>
    public const string PolicyDecisionTag = "policy.decision";

    /// <summary>The policy denial reason attribute.</summary>
    public const string PolicyReasonTag = "policy.reason";

    /// <summary>The compile outcome attribute.</summary>
    public const string CompileOutcomeTag = "compile.outcome";

    /// <summary>The number of error diagnostics attribute.</summary>
    public const string CompileDiagnosticsTag = "compile.diagnostics";

    /// <summary>The terminal sandbox outcome attribute.</summary>
    public const string SandboxOutcomeTag = "sandbox.outcome";

    /// <summary>The container exit code attribute.</summary>
    public const string SandboxExitCodeTag = "sandbox.exit_code";

    /// <summary>Whether the worker wrote a result file.</summary>
    public const string SandboxResultWrittenTag = "sandbox.result_written";

    /// <summary>The kill reason attribute; <c>wall</c>, <c>cancelled</c>, or <c>crash</c>.</summary>
    public const string SandboxKillReasonTag = "sandbox.kill_reason";

    /// <summary>The effective wall-clock limit in seconds.</summary>
    public const string SandboxLimitWallTag = "sandbox.limit.wall_seconds";

    /// <summary>The effective CPU core limit.</summary>
    public const string SandboxLimitCpuTag = "sandbox.limit.cpu_cores";

    /// <summary>The effective memory limit in bytes.</summary>
    public const string SandboxLimitMemoryTag = "sandbox.limit.memory_bytes";

    /// <summary>The effective process limit.</summary>
    public const string SandboxLimitPidsTag = "sandbox.limit.pids";

    /// <summary>The effective output byte limit.</summary>
    public const string SandboxLimitOutputTag = "sandbox.limit.output_bytes";

    /// <summary>The wall time used in seconds.</summary>
    public const string SandboxUsedWallTag = "sandbox.used.wall_seconds";

    /// <summary>The result size in bytes.</summary>
    public const string SandboxUsedOutputTag = "sandbox.used.output_bytes";

    internal const string CompileSpanName = "compile csharp";
    internal const string RunSpanName = "sandbox run";
    internal const string ContainerBackend = "container";
    internal const string AllowDecision = "allow";
    internal const string DenyDecision = "deny";
}
