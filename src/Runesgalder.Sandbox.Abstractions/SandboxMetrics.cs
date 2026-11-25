using System.Diagnostics.Metrics;

namespace Runesgalder.Sandbox;

/// <summary>Emits the sandbox metrics; a no-op unless a host registers the meter.</summary>
internal static class SandboxMetrics
{
    private static readonly Meter Meter = new("Runesgalder.Sandbox", "0.1.0");

    private static readonly Counter<long> Runs = Meter.CreateCounter<long>(
        "sandbox.runs",
        "{run}",
        "Accepted runs that reached a terminal state.");

    private static readonly Histogram<double> RunDuration = Meter.CreateHistogram<double>(
        "sandbox.run.duration",
        "s",
        "Run duration in seconds.");

    private static readonly Histogram<double> QueueWait = Meter.CreateHistogram<double>(
        "sandbox.queue.wait",
        "s",
        "Time from acceptance to container start.");

    private static readonly Counter<long> CompileRuns = Meter.CreateCounter<long>(
        "sandbox.compile.runs",
        "{compilation}",
        "Compilations that ended.");

    private static readonly Histogram<double> CompileDuration = Meter.CreateHistogram<double>(
        "sandbox.compile.duration",
        "s",
        "Compilation duration in seconds.");

    private static readonly Counter<long> PolicyDecisions = Meter.CreateCounter<long>(
        "sandbox.policy.decisions",
        "{decision}",
        "Security-policy decisions.");

    private static readonly Histogram<long> OutputBytes = Meter.CreateHistogram<long>(
        "sandbox.output.bytes",
        "By",
        "Result file size in bytes.");

    private static readonly Counter<long> Kills = Meter.CreateCounter<long>(
        "sandbox.kills",
        "{kill}",
        "Containers killed by the runner.");

    public static void RecordRun(string outcome, TimeSpan duration)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new(SandboxDiagnostics.SandboxBackendTag, SandboxDiagnostics.ContainerBackend),
            new(SandboxDiagnostics.SandboxOutcomeTag, outcome),
        };
        Runs.Add(1, tags);
        RunDuration.Record(duration.TotalSeconds, tags);
    }

    public static void RecordQueueWait(TimeSpan wait) =>
        QueueWait.Record(
            wait.TotalSeconds,
            new KeyValuePair<string, object?>(
                SandboxDiagnostics.SandboxBackendTag,
                SandboxDiagnostics.ContainerBackend));

    public static void RecordCompile(string outcome, TimeSpan duration)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new(SandboxDiagnostics.CompileOutcomeTag, outcome),
        };
        CompileRuns.Add(1, tags);
        CompileDuration.Record(duration.TotalSeconds, tags);
    }

    public static void RecordPolicyDecision(string decision, string? reason)
    {
        var tags = new List<KeyValuePair<string, object?>>
        {
            new(SandboxDiagnostics.PolicyDecisionTag, decision),
        };
        if (reason is not null)
        {
            tags.Add(new(SandboxDiagnostics.PolicyReasonTag, reason));
        }

        PolicyDecisions.Add(1, tags.ToArray());
    }

    public static void RecordOutputBytes(long bytes, string outcome) =>
        OutputBytes.Record(
            bytes,
            new KeyValuePair<string, object?>(SandboxDiagnostics.SandboxBackendTag, SandboxDiagnostics.ContainerBackend),
            new KeyValuePair<string, object?>(SandboxDiagnostics.SandboxOutcomeTag, outcome));

    public static void RecordKill(string reason) =>
        Kills.Add(1, new KeyValuePair<string, object?>(SandboxDiagnostics.SandboxKillReasonTag, reason));
}
