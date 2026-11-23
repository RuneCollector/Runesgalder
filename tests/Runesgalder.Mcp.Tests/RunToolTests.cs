using System.Text.Json;
using Runesgalder.Mcp;

namespace Runesgalder.McpTests;

public sealed class RunToolTests
{
    [Fact]
    public async Task RunsAnAnalysisAndReturnsTheEnvelope()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new
            {
                code = Harness.ValidScript,
                data = new { name = "sales", csv = "region,amount\nnorth,1\nsouth,2\n" },
                wait_seconds = 30
            }),
            CancellationToken.None);

        Assert.False(result.IsError);
        var payload = Harness.Structured(result);
        Assert.Equal("succeeded", payload.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("error").ValueKind);

        var runId = payload.GetProperty("run_id").GetString();
        Assert.StartsWith("run_", runId, StringComparison.Ordinal);
        Assert.Equal(20, runId!.Length);

        var analysis = payload.GetProperty("result");
        Assert.Equal("ok", analysis.GetProperty("summary").GetString());
        Assert.Equal("hello", analysis.GetProperty("logs")[0].GetString());
        var chart = analysis.GetProperty("charts")[0];
        Assert.Equal("bar", chart.GetProperty("kind").GetString());
        Assert.Equal("Rows", chart.GetProperty("title").GetString());
        Assert.Equal(1, chart.GetProperty("values")[0].GetDouble());

        var resource = payload.GetProperty("resource");
        Assert.Equal(12.5, resource.GetProperty("sandbox_ms").GetDouble());
        Assert.Equal(128, resource.GetProperty("output_bytes").GetInt64());
        Assert.True(resource.GetProperty("compile_ms").GetDouble() >= 0);
        Assert.True(resource.GetProperty("wall_ms").GetDouble() >= 0);

        var snapshot = harness.Runs.Get(runId);
        Assert.NotNull(snapshot);
        Assert.Equal("succeeded", snapshot.Status);
        Assert.Equal("ok", snapshot.Result!.Summary);
    }

    [Fact]
    public async Task AcceptsOmittedData()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new { code = Harness.ValidScript, wait_seconds = 30 }),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("succeeded", Harness.Structured(result).GetProperty("status").GetString());
    }

    [Fact]
    public async Task ReturnsImmediatelyWhenWaitIsZero()
    {
        var executor = new FakeSandboxExecutor { Block = true };
        await using var harness = new Harness(executor: executor);

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new { code = Harness.ValidScript, wait_seconds = 0 }),
            CancellationToken.None);

        Assert.False(result.IsError);
        var payload = Harness.Structured(result);
        var status = payload.GetProperty("status").GetString();
        Assert.True(status is RunStatus.Queued or RunStatus.Running);
        var runId = payload.GetProperty("run_id").GetString()!;

        var cancelled = await harness.Tools.CallAsync(
            "cancel_run",
            Harness.Args(new { run_id = runId }),
            CancellationToken.None);

        Assert.False(cancelled.IsError);
        var cancelStatus = Harness.Structured(cancelled).GetProperty("status").GetString();
        Assert.True(cancelStatus is RunStatus.Queued or RunStatus.Running or RunStatus.Cancelled);
    }

    [Fact]
    public async Task ReportsServerBusyWhenTheQueueIsFull()
    {
        var executor = new FakeSandboxExecutor { Block = true };
        await using var harness = new Harness(gateCapacity: 1, executor: executor);

        var first = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new { code = Harness.ValidScript, wait_seconds = 0 }),
            CancellationToken.None);
        Assert.False(first.IsError);

        var second = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new { code = Harness.ValidScript, wait_seconds = 0 }),
            CancellationToken.None);

        Assert.True(second.IsError);
        var payload = Harness.Structured(second);
        Assert.Equal(RunStatus.Failed, payload.GetProperty("status").GetString());
        Assert.Equal("server_busy", payload.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("run_id").ValueKind);

        var runId = Harness.Structured(first).GetProperty("run_id").GetString()!;
        await harness.Tools.CallAsync(
            "cancel_run",
            Harness.Args(new { run_id = runId }),
            CancellationToken.None);
    }

    [Fact]
    public async Task ReportsACompileFailureWithoutStartingTheSandbox()
    {
        var executor = new FakeSandboxExecutor();
        await using var harness = new Harness(executor: executor);

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new { code = "public sealed class Analysis { }", wait_seconds = 30 }),
            CancellationToken.None);

        Assert.True(result.IsError);
        var payload = Harness.Structured(result);
        Assert.Equal(RunStatus.Failed, payload.GetProperty("status").GetString());
        Assert.Equal("contract_invalid", Harness.ErrorCode(result));
        Assert.True(payload.GetProperty("diagnostics").GetArrayLength() > 0);
        Assert.Equal(0, executor.Calls);
    }

    [Theory]
    [InlineData("timeout", RunStatus.TimedOut, "run_timeout")]
    [InlineData("output-quota", RunStatus.Failed, "policy_denied")]
    [InlineData("contract", RunStatus.Failed, "contract_invalid")]
    [InlineData("script", RunStatus.Failed, "internal")]
    [InlineData("input", RunStatus.Failed, "invalid_request")]
    [InlineData("unknown-kind", RunStatus.Failed, "internal")]
    public async Task MapsWorkerFailures(string workerError, string status, string errorCode)
    {
        var executor = new FakeSandboxExecutor
        {
            Outcome = FakeSandboxExecutor.Failure(workerError, "worker said no")
        };
        await using var harness = new Harness(executor: executor);

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new { code = Harness.ValidScript, wait_seconds = 30 }),
            CancellationToken.None);

        Assert.True(result.IsError);
        var payload = Harness.Structured(result);
        Assert.Equal(status, payload.GetProperty("status").GetString());
        Assert.Equal(errorCode, Harness.ErrorCode(result));
        Assert.Equal("worker said no", payload.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task MapsWorkerInputErrorsAboutDataRowsToDataTooLarge()
    {
        var executor = new FakeSandboxExecutor
        {
            Outcome = FakeSandboxExecutor.Failure(
                "input",
                "The CSV file has more than 100000 data rows.")
        };
        await using var harness = new Harness(executor: executor);

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new { code = Harness.ValidScript, wait_seconds = 30 }),
            CancellationToken.None);

        Assert.Equal("data_too_large", Harness.ErrorCode(result));
    }

    [Fact]
    public async Task ReportsSandboxUnavailableWhenNoResultIsWritten()
    {
        var executor = new FakeSandboxExecutor
        {
            Outcome = new SandboxOutcome(TimedOut: false, ResultWritten: false, ResultJson: null, OutputBytes: 0)
        };
        await using var harness = new Harness(executor: executor);

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new { code = Harness.ValidScript, wait_seconds = 30 }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(RunStatus.Failed, Harness.Structured(result).GetProperty("status").GetString());
        Assert.Equal("sandbox_unavailable", Harness.ErrorCode(result));
    }

    [Fact]
    public async Task ReportsTimeoutWhenTheContainerIsKilled()
    {
        var executor = new FakeSandboxExecutor
        {
            Outcome = new SandboxOutcome(TimedOut: true, ResultWritten: false, ResultJson: null, OutputBytes: 0)
        };
        await using var harness = new Harness(executor: executor);

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new { code = Harness.ValidScript, wait_seconds = 30 }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(RunStatus.TimedOut, Harness.Structured(result).GetProperty("status").GetString());
        Assert.Equal("run_timeout", Harness.ErrorCode(result));
    }

    [Fact]
    public async Task TreatsAnUnreadableResultAsSandboxUnavailable()
    {
        var executor = new FakeSandboxExecutor
        {
            Outcome = new SandboxOutcome(false, ResultWritten: true, ResultJson: "{not json", OutputBytes: 8)
        };
        await using var harness = new Harness(executor: executor);

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new { code = Harness.ValidScript, wait_seconds = 30 }),
            CancellationToken.None);

        Assert.Equal("sandbox_unavailable", Harness.ErrorCode(result));
    }

    [Fact]
    public async Task RejectsOversizedData()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new
            {
                code = Harness.ValidScript,
                data = new { csv = new string('a', McpLimits.MaxDataBytes + 1) }
            }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("data_too_large", Harness.ErrorCode(result));
    }

    [Fact]
    public async Task RejectsAPathLikeTableName()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new
            {
                code = Harness.ValidScript,
                data = new { name = "sales/2026", csv = "a\n1\n" }
            }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("invalid_request", Harness.ErrorCode(result));
        Assert.Contains(
            "not a path",
            Harness.Structured(result).GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task RejectsAnOverlongTableName()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new
            {
                code = Harness.ValidScript,
                data = new { name = new string('a', 129), csv = "a\n1\n" }
            }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("invalid_request", Harness.ErrorCode(result));
    }

    [Fact]
    public async Task RejectsNonStringCsv()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new { code = Harness.ValidScript, data = new { csv = 42 } }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("invalid_request", Harness.ErrorCode(result));
    }

    [Fact]
    public async Task RejectsUnknownRunFields()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new { code = Harness.ValidScript, filename = "data.csv" }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("invalid_request", Harness.ErrorCode(result));
    }

    [Theory]
    [InlineData("{\"code\":\"x\",\"wait_seconds\":121}")]
    [InlineData("{\"code\":\"x\",\"wait_seconds\":-1}")]
    [InlineData("{\"code\":\"x\",\"wait_seconds\":1.5}")]
    [InlineData("{\"code\":\"x\",\"wait_seconds\":\"30\"}")]
    [InlineData("{\"code\":\"x\",\"wall_clock_seconds\":0}")]
    [InlineData("{\"code\":\"x\",\"wall_clock_seconds\":121}")]
    [InlineData("{\"code\":\"x\",\"wall_clock_seconds\":\"fast\"}")]
    public async Task RejectsInvalidTimingValues(string json)
    {
        await using var harness = new Harness();
        using var document = JsonDocument.Parse(json);
        var arguments = document.RootElement
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);

        var result = await harness.Tools.CallAsync("run_csharp", arguments, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("invalid_request", Harness.ErrorCode(result));
    }

    [Fact]
    public async Task RejectsAWallClockAboveTheConfiguredMaximum()
    {
        var options = new McpOptions { MaxWallClock = TimeSpan.FromSeconds(60) };
        await using var harness = new Harness(options: options);

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new { code = Harness.ValidScript, wall_clock_seconds = 61 }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("invalid_request", Harness.ErrorCode(result));
    }
}
