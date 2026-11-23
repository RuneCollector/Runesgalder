using System.Text.Json;
using Runesgalder.Mcp;

namespace Runesgalder.McpTests;

public sealed class GetAndCancelToolTests
{
    [Fact]
    public async Task GetRunReturnsTheStoredEnvelope()
    {
        await using var harness = new Harness();
        var runId = await RunOnceAsync(harness);

        var result = await harness.Tools.CallAsync(
            "get_run",
            Harness.Args(new { run_id = runId }),
            CancellationToken.None);

        Assert.False(result.IsError);
        var payload = Harness.Structured(result);
        Assert.Equal(runId, payload.GetProperty("run_id").GetString());
        Assert.Equal(RunStatus.Succeeded, payload.GetProperty("status").GetString());
        Assert.Equal("ok", payload.GetProperty("result").GetProperty("summary").GetString());
    }

    [Fact]
    public async Task GetRunReportsRunNotFound()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "get_run",
            Harness.Args(new { run_id = "run_missing" }),
            CancellationToken.None);

        Assert.True(result.IsError);
        var payload = Harness.Structured(result);
        Assert.Equal(RunStatus.Failed, payload.GetProperty("status").GetString());
        Assert.Equal("run_not_found", Harness.ErrorCode(result));
    }

    [Fact]
    public async Task GetRunRejectsAMissingIdentifier()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "get_run",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("invalid_request", Harness.ErrorCode(result));
    }

    [Fact]
    public async Task GetRunRejectsUnknownFields()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "get_run",
            Harness.Args(new { run_id = "run_missing", verbose = true }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("invalid_request", Harness.ErrorCode(result));
    }

    [Fact]
    public async Task CancelRunReturnsTheTerminalStatusWithoutAnError()
    {
        await using var harness = new Harness();
        var runId = await RunOnceAsync(harness);

        var result = await harness.Tools.CallAsync(
            "cancel_run",
            Harness.Args(new { run_id = runId }),
            CancellationToken.None);

        Assert.False(result.IsError);
        var payload = Harness.Structured(result);
        Assert.Equal(runId, payload.GetProperty("run_id").GetString());
        Assert.Equal(RunStatus.Succeeded, payload.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task CancelRunReportsRunNotFound()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "cancel_run",
            Harness.Args(new { run_id = "run_missing" }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("run_not_found", Harness.ErrorCode(result));
    }

    [Fact]
    public async Task CancelRunRejectsAnOverlongIdentifier()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "cancel_run",
            Harness.Args(new { run_id = new string('a', 129) }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("invalid_request", Harness.ErrorCode(result));
    }

    private static async Task<string> RunOnceAsync(Harness harness)
    {
        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new { code = Harness.ValidScript, wait_seconds = 30 }),
            CancellationToken.None);

        Assert.False(result.IsError);
        return Harness.Structured(result).GetProperty("run_id").GetString()!;
    }
}
