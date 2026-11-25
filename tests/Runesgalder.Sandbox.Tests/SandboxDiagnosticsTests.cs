using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Runesgalder.Sandbox;
using Runesgalder.Sandbox.Compilation;

namespace Runesgalder.SandboxTests;

public sealed class SandboxDiagnosticsTests
{
    private const string ValidScript = """
        using System.Threading.Tasks;
        using Runesgalder.Sandbox;

        public sealed class Analysis : IAnalysis
        {
            public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
            {
                context.Log(table.Name);
                return Task.FromResult(new AnalysisResult("rows", []));
            }
        }
        """;

    [Fact]
    public void RecordsASuccessfulCompilation()
    {
        var spans = new List<Activity>();
        using var listener = ListenToSpans(spans);
        using var metrics = new MetricCapture();
        using var marker = StartMarker();

        var result = new ScriptCompiler().Compile(ValidScript);

        Assert.True(result.Success);
        var span = Assert.Single(SpansOf(spans, marker));
        Assert.Equal("compile csharp", span.OperationName);
        Assert.Equal("allow", span.GetTagItem(SandboxDiagnostics.PolicyDecisionTag));
        Assert.Equal("ok", span.GetTagItem(SandboxDiagnostics.CompileOutcomeTag));
        Assert.Equal(0, span.GetTagItem(SandboxDiagnostics.CompileDiagnosticsTag));
        Assert.Null(span.GetTagItem(SandboxDiagnostics.PolicyReasonTag));
        Assert.NotEqual(ActivityStatusCode.Error, span.Status);

        var compile = Assert.Single(metrics.For("sandbox.compile.runs", marker));
        Assert.Equal("ok", compile.Tags[SandboxDiagnostics.CompileOutcomeTag]);
        Assert.Single(metrics.For("sandbox.compile.duration", marker));
        Assert.Empty(metrics.For("sandbox.policy.decisions", marker));
    }

    [Fact]
    public void RecordsABannedNamespaceDenial()
    {
        var spans = new List<Activity>();
        using var listener = ListenToSpans(spans);
        using var metrics = new MetricCapture();
        using var marker = StartMarker();

        var result = new ScriptCompiler().Compile("""
            using System.IO;
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context) =>
                    Task.FromResult(new AnalysisResult("rows", []));
            }
            """);

        Assert.False(result.Success);
        var span = Assert.Single(SpansOf(spans, marker));
        Assert.Equal("deny", span.GetTagItem(SandboxDiagnostics.PolicyDecisionTag));
        Assert.Equal("policy_denied", span.GetTagItem(SandboxDiagnostics.PolicyReasonTag));
        Assert.Equal("policy_denied", span.GetTagItem(SandboxDiagnostics.CompileOutcomeTag));
        Assert.Equal(1, span.GetTagItem(SandboxDiagnostics.CompileDiagnosticsTag));
        Assert.Equal(ActivityStatusCode.Error, span.Status);

        var compile = Assert.Single(metrics.For("sandbox.compile.runs", marker));
        Assert.Equal("policy_denied", compile.Tags[SandboxDiagnostics.CompileOutcomeTag]);
        var decision = Assert.Single(metrics.For("sandbox.policy.decisions", marker));
        Assert.Equal("deny", decision.Tags[SandboxDiagnostics.PolicyDecisionTag]);
        Assert.Equal("policy_denied", decision.Tags[SandboxDiagnostics.PolicyReasonTag]);
    }

    [Fact]
    public void RecordsAContractFailure()
    {
        var spans = new List<Activity>();
        using var listener = ListenToSpans(spans);
        using var metrics = new MetricCapture();
        using var marker = StartMarker();

        var result = new ScriptCompiler().Compile("public sealed class NotAnAnalysis { }");

        Assert.False(result.Success);
        var span = Assert.Single(SpansOf(spans, marker));
        Assert.Equal("allow", span.GetTagItem(SandboxDiagnostics.PolicyDecisionTag));
        Assert.Equal("contract_invalid", span.GetTagItem(SandboxDiagnostics.CompileOutcomeTag));
        Assert.Null(span.GetTagItem(SandboxDiagnostics.PolicyReasonTag));

        var compile = Assert.Single(metrics.For("sandbox.compile.runs", marker));
        Assert.Equal("contract_invalid", compile.Tags[SandboxDiagnostics.CompileOutcomeTag]);
        Assert.Empty(metrics.For("sandbox.policy.decisions", marker));
    }

    [Fact]
    public void RecordsAnOversizedSourceAsAnAdmissionDenial()
    {
        var spans = new List<Activity>();
        using var listener = ListenToSpans(spans);
        using var metrics = new MetricCapture();
        using var marker = StartMarker();
        var compiler = new ScriptCompiler(new ScriptCompilerOptions { MaxSourceBytes = 16 });

        var result = compiler.Compile(ValidScript);

        Assert.False(result.Success);
        var span = Assert.Single(SpansOf(spans, marker));
        Assert.Equal("deny", span.GetTagItem(SandboxDiagnostics.PolicyDecisionTag));
        Assert.Equal("source_too_large", span.GetTagItem(SandboxDiagnostics.PolicyReasonTag));
        Assert.Equal("source_too_large", span.GetTagItem(SandboxDiagnostics.CompileOutcomeTag));

        var decision = Assert.Single(metrics.For("sandbox.policy.decisions", marker));
        Assert.Equal("source_too_large", decision.Tags[SandboxDiagnostics.PolicyReasonTag]);
    }

    [Fact]
    public void SpansCarryNoSourceText()
    {
        var spans = new List<Activity>();
        using var listener = ListenToSpans(spans);
        using var marker = StartMarker();
        const string payloadMarker = "payload-marker-4b7";
        var source = $$"""
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            // {{payloadMarker}}
            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context) =>
                    Task.FromResult(new AnalysisResult("{{payloadMarker}}", []));
            }
            """;

        var result = new ScriptCompiler().Compile(source);

        Assert.True(result.Success);
        var diagnosticsSpans = SpansOf(spans, marker);
        Assert.NotEmpty(diagnosticsSpans);
        foreach (var span in diagnosticsSpans)
        {
            foreach (var tag in span.TagObjects)
            {
                Assert.DoesNotContain(
                    payloadMarker,
                    Convert.ToString(tag.Value, CultureInfo.InvariantCulture),
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void RecordsContainerKillsByReason()
    {
        using var metrics = new MetricCapture();

        SandboxMetrics.RecordKill("wall");
        SandboxMetrics.RecordKill("cancelled");

        var kills = metrics.For("sandbox.kills");
        Assert.Contains(kills, kill => Equals(kill.Tags[SandboxDiagnostics.SandboxKillReasonTag], "wall"));
        Assert.Contains(kills, kill => Equals(kill.Tags[SandboxDiagnostics.SandboxKillReasonTag], "cancelled"));
    }

    private static Activity StartMarker() =>
        new Activity("diagnostics-test").SetIdFormat(ActivityIdFormat.W3C).Start();

    private static Activity[] SpansOf(List<Activity> spans, Activity marker)
    {
        lock (spans)
        {
            return spans
                .Where(span => span.TraceId == marker.TraceId &&
                    span.Source.Name == "Runesgalder.Sandbox")
                .ToArray();
        }
    }

    private static ActivityListener ListenToSpans(List<Activity> spans)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Runesgalder.Sandbox",
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
                    if (instrument.Meter.Name == "Runesgalder.Sandbox")
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

        public Measurement[] For(string instrument, Activity? scope = null)
        {
            lock (_gate)
            {
                return _measurements
                    .Where(item => item.Instrument == instrument &&
                        (scope is null || item.TraceId == scope.TraceId))
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

            var traceId = Activity.Current?.TraceId ?? default;
            lock (_gate)
            {
                _measurements.Add(new Measurement(instrument.Name, value, dictionary, traceId));
            }
        }
    }

    private sealed record Measurement(
        string Instrument,
        double Value,
        Dictionary<string, object?> Tags,
        ActivityTraceId TraceId);
}
