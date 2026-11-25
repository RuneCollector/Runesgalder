using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Runesgalder.Mcp;
using Runesgalder.Sandbox;

namespace Runesgalder.McpTests;

public sealed class RunDiagnosticsTests
{
    [Fact]
    public async Task RecordsASuccessfulRun()
    {
        var spans = new List<Activity>();
        using var listener = ListenToSpans(spans);
        using var metrics = new MetricCapture();
        await using var harness = new Harness();

        var record = harness.Runs.TrySubmit(Harness.ValidScript, "a\n1", TimeSpan.FromSeconds(30));
        Assert.NotNull(record);
        await record.TerminalTask.WaitAsync(TimeSpan.FromSeconds(10));

        var span = await WaitForRunSpanAsync(spans, record.Id);
        Assert.Equal("container", span.GetTagItem(SandboxDiagnostics.SandboxBackendTag));
        Assert.Equal("allow", span.GetTagItem(SandboxDiagnostics.PolicyDecisionTag));
        Assert.Equal("ok", span.GetTagItem(SandboxDiagnostics.SandboxOutcomeTag));
        Assert.Equal(0, span.GetTagItem(SandboxDiagnostics.SandboxExitCodeTag));
        Assert.Equal(true, span.GetTagItem(SandboxDiagnostics.SandboxResultWrittenTag));
        Assert.Null(span.GetTagItem(SandboxDiagnostics.SandboxKillReasonTag));
        Assert.Equal(30, TagDouble(span, SandboxDiagnostics.SandboxLimitWallTag));
        Assert.Equal(McpLimits.MaxCpuCores, span.GetTagItem(SandboxDiagnostics.SandboxLimitCpuTag));
        Assert.Equal(McpLimits.MaxMemoryBytes, span.GetTagItem(SandboxDiagnostics.SandboxLimitMemoryTag));
        Assert.Equal(McpLimits.MaxProcesses, span.GetTagItem(SandboxDiagnostics.SandboxLimitPidsTag));
        Assert.Equal(McpLimits.MaxOutputBytes, span.GetTagItem(SandboxDiagnostics.SandboxLimitOutputTag));
        Assert.Equal(128L, span.GetTagItem(SandboxDiagnostics.SandboxUsedOutputTag));
        Assert.NotEqual(ActivityStatusCode.Error, span.Status);

        Assert.Contains(metrics.For("sandbox.runs"), item =>
            Equals(item.Tags[SandboxDiagnostics.SandboxOutcomeTag], "ok"));
        Assert.Contains(metrics.For("sandbox.output.bytes"), item =>
            item.Value == 128 && Equals(item.Tags[SandboxDiagnostics.SandboxOutcomeTag], "ok"));
        Assert.NotEmpty(metrics.For("sandbox.queue.wait"));
    }

    [Fact]
    public async Task RecordsATimeoutKill()
    {
        var spans = new List<Activity>();
        using var listener = ListenToSpans(spans);
        var executor = new FakeSandboxExecutor
        {
            Outcome = new SandboxOutcome(true, false, null, 0, -1)
        };
        await using var harness = new Harness(executor: executor);

        var record = harness.Runs.TrySubmit(Harness.ValidScript, string.Empty, TimeSpan.FromSeconds(30));
        Assert.NotNull(record);
        await record.TerminalTask.WaitAsync(TimeSpan.FromSeconds(10));

        var span = await WaitForRunSpanAsync(spans, record.Id);
        Assert.Equal("run_timeout", span.GetTagItem(SandboxDiagnostics.SandboxOutcomeTag));
        Assert.Equal("wall", span.GetTagItem(SandboxDiagnostics.SandboxKillReasonTag));
        Assert.Equal(-1, span.GetTagItem(SandboxDiagnostics.SandboxExitCodeTag));
        Assert.Equal(false, span.GetTagItem(SandboxDiagnostics.SandboxResultWrittenTag));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public async Task RecordsACancelledKill()
    {
        var spans = new List<Activity>();
        using var listener = ListenToSpans(spans);
        var executor = new FakeSandboxExecutor { Block = true };
        await using var harness = new Harness(executor: executor);

        var record = harness.Runs.TrySubmit(Harness.ValidScript, string.Empty, TimeSpan.FromSeconds(30));
        Assert.NotNull(record);
        await executor.Started.WaitAsync(TimeSpan.FromSeconds(5));
        harness.Runs.Cancel(record.Id);
        await record.TerminalTask.WaitAsync(TimeSpan.FromSeconds(5));

        var span = await WaitForRunSpanAsync(spans, record.Id);
        Assert.Equal("cancelled", span.GetTagItem(SandboxDiagnostics.SandboxOutcomeTag));
        Assert.Equal("cancelled", span.GetTagItem(SandboxDiagnostics.SandboxKillReasonTag));
    }

    [Fact]
    public async Task RecordsAMissingSandbox()
    {
        var spans = new List<Activity>();
        using var listener = ListenToSpans(spans);
        var executor = new FakeSandboxExecutor
        {
            Outcome = new SandboxOutcome(false, false, null, 0, 125)
        };
        await using var harness = new Harness(executor: executor);

        var record = harness.Runs.TrySubmit(Harness.ValidScript, string.Empty, TimeSpan.FromSeconds(30));
        Assert.NotNull(record);
        await record.TerminalTask.WaitAsync(TimeSpan.FromSeconds(10));

        var span = await WaitForRunSpanAsync(spans, record.Id);
        Assert.Equal("sandbox_unavailable", span.GetTagItem(SandboxDiagnostics.SandboxOutcomeTag));
        Assert.Equal(false, span.GetTagItem(SandboxDiagnostics.SandboxResultWrittenTag));
        Assert.Equal(125, span.GetTagItem(SandboxDiagnostics.SandboxExitCodeTag));
        Assert.Null(span.GetTagItem(SandboxDiagnostics.SandboxUsedOutputTag));
    }

    [Fact]
    public async Task RecordsACompilePolicyDenialOnTheRunSpan()
    {
        var spans = new List<Activity>();
        using var listener = ListenToSpans(spans);
        await using var harness = new Harness();

        var record = harness.Runs.TrySubmit(BannedScript, string.Empty, TimeSpan.FromSeconds(30));
        Assert.NotNull(record);
        await record.TerminalTask.WaitAsync(TimeSpan.FromSeconds(10));

        var span = await WaitForRunSpanAsync(spans, record.Id);
        Assert.Equal("deny", span.GetTagItem(SandboxDiagnostics.PolicyDecisionTag));
        Assert.Equal("policy_denied", span.GetTagItem(SandboxDiagnostics.PolicyReasonTag));
        Assert.Equal("policy_denied", span.GetTagItem(SandboxDiagnostics.SandboxOutcomeTag));
        Assert.Null(span.GetTagItem(SandboxDiagnostics.SandboxExitCodeTag));
    }

    [Fact]
    public async Task RecordsAnAdmissionDenialWithoutARunIdentifier()
    {
        var spans = new List<Activity>();
        using var listener = ListenToSpans(spans);
        using var metrics = new MetricCapture();
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new { code = new string('x', McpLimits.MaxSourceBytes + 1) }),
            CancellationToken.None);

        Assert.True(result.IsError);
        var span = await WaitForRunSpanAsync(
            spans,
            predicate: activity => Equals(
                activity.GetTagItem(SandboxDiagnostics.SandboxOutcomeTag),
                "source_too_large"));
        Assert.Null(span.GetTagItem(SandboxDiagnostics.RunIdTag));
        Assert.Equal("deny", span.GetTagItem(SandboxDiagnostics.PolicyDecisionTag));
        Assert.Equal("source_too_large", span.GetTagItem(SandboxDiagnostics.PolicyReasonTag));
        Assert.Equal(ActivityStatusCode.Error, span.Status);

        Assert.Contains(metrics.For("sandbox.policy.decisions"), item =>
            Equals(item.Tags[SandboxDiagnostics.PolicyReasonTag], "source_too_large"));
    }

    [Fact]
    public async Task SpansCarryNoSourceOrDataText()
    {
        var spans = new List<Activity>();
        using var listener = ListenToSpans(spans);
        await using var harness = new Harness();
        const string marker = "payload-marker-a1c";
        var script = Harness.ValidScript.Replace(
            "context.Log(table.Name);",
            $"context.Log(\"{marker}\");",
            StringComparison.Ordinal);

        var record = harness.Runs.TrySubmit(script, $"column\n{marker}", TimeSpan.FromSeconds(30));
        Assert.NotNull(record);
        await record.TerminalTask.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitForRunSpanAsync(spans, record.Id);

        lock (spans)
        {
            Assert.NotEmpty(spans);
            foreach (var span in spans)
            {
                foreach (var tag in span.TagObjects)
                {
                    Assert.DoesNotContain(
                        marker,
                        Convert.ToString(tag.Value, CultureInfo.InvariantCulture),
                        StringComparison.Ordinal);
                }
            }
        }
    }

    private const string BannedScript = """
        using System.IO;
        using System.Threading.Tasks;
        using Runesgalder.Sandbox;

        public sealed class Analysis : IAnalysis
        {
            public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context) =>
                Task.FromResult(new AnalysisResult("rows", []));
        }
        """;

    private static double TagDouble(Activity activity, string tag) =>
        activity.GetTagItem(tag) is { } value
            ? Convert.ToDouble(value, CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"The span has no '{tag}' tag.");

    private static async Task<Activity> WaitForRunSpanAsync(
        List<Activity> spans,
        string runId) =>
        await WaitForRunSpanAsync(
            spans,
            activity => Equals(activity.GetTagItem(SandboxDiagnostics.RunIdTag), runId));

    private static async Task<Activity> WaitForRunSpanAsync(
        List<Activity> spans,
        Func<Activity, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (spans)
            {
                var found = spans.FirstOrDefault(activity =>
                    activity.OperationName == "sandbox run" && predicate(activity));
                if (found is not null)
                {
                    return found;
                }
            }

            await Task.Delay(10);
        }

        throw new InvalidOperationException("The sandbox run span was not recorded.");
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

        public Measurement[] For(string instrument)
        {
            lock (_gate)
            {
                return _measurements.Where(item => item.Instrument == instrument).ToArray();
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

    private sealed record Measurement(string Instrument, double Value, Dictionary<string, object?> Tags);
}
