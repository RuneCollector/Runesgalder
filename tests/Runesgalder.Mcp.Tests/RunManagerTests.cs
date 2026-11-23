using Microsoft.Extensions.Logging.Abstractions;
using Runesgalder.Mcp;
using Runesgalder.Sandbox.Compilation;

namespace Runesgalder.McpTests;

public sealed class RunManagerTests
{
    [Fact]
    public async Task CancelCompletesAQueuedRunImmediately()
    {
        var executor = new FakeSandboxExecutor { Block = true };
        await using var harness = new Harness(gateCapacity: 2, executor: executor);

        var running = harness.Runs.TrySubmit(Harness.ValidScript, string.Empty, TimeSpan.FromSeconds(30));
        Assert.NotNull(running);
        await executor.Started.WaitAsync(TimeSpan.FromSeconds(5));

        var queued = harness.Runs.TrySubmit(Harness.ValidScript, string.Empty, TimeSpan.FromSeconds(30));
        Assert.NotNull(queued);

        var cancelled = harness.Runs.Cancel(queued.Id);

        Assert.NotNull(cancelled);
        Assert.Equal(RunStatus.Cancelled, cancelled.Status);
        Assert.Equal("cancelled", cancelled.Error!.Code);
        Assert.True(queued.IsTerminal);

        harness.Runs.Cancel(running.Id);
        await running.TerminalTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancelStopsARunningRun()
    {
        var executor = new FakeSandboxExecutor { Block = true };
        await using var harness = new Harness(executor: executor);

        var record = harness.Runs.TrySubmit(Harness.ValidScript, string.Empty, TimeSpan.FromSeconds(30));
        Assert.NotNull(record);
        await executor.Started.WaitAsync(TimeSpan.FromSeconds(5));

        var cancelled = harness.Runs.Cancel(record.Id);
        Assert.NotNull(cancelled);
        Assert.Equal("cancelled", cancelled.Error!.Code);

        await record.TerminalTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(RunStatus.Cancelled, record.Snapshot().Status);
        Assert.Equal(1, executor.Calls);
    }

    [Fact]
    public async Task CancelKeepsTheStatusOfATerminalRun()
    {
        await using var harness = new Harness();

        var record = harness.Runs.TrySubmit(Harness.ValidScript, string.Empty, TimeSpan.FromSeconds(30));
        Assert.NotNull(record);
        await record.TerminalTask.WaitAsync(TimeSpan.FromSeconds(10));

        var first = harness.Runs.Cancel(record.Id);
        var second = harness.Runs.Cancel(record.Id);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(RunStatus.Succeeded, first.Status);
        Assert.Equal(RunStatus.Succeeded, second.Status);
        Assert.Null(first.Error);
    }

    [Fact]
    public async Task CancelReturnsNullForAnUnknownRun()
    {
        await using var harness = new Harness();

        Assert.Null(harness.Runs.Cancel("run_missing"));
        Assert.Null(harness.Runs.Get("run_missing"));
    }

    [Fact]
    public async Task RetainsTerminalRunsForThirtyMinutes()
    {
        await using var harness = new Harness();

        var record = harness.Runs.TrySubmit(Harness.ValidScript, string.Empty, TimeSpan.FromSeconds(30));
        Assert.NotNull(record);
        await record.TerminalTask.WaitAsync(TimeSpan.FromSeconds(10));

        harness.Time.Advance(TimeSpan.FromMinutes(29));
        Assert.NotNull(harness.Runs.Get(record.Id));

        harness.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(harness.Runs.Get(record.Id));
    }

    [Fact]
    public async Task EvictsTheOldestTerminalRunWhenTheCapacityIsReached()
    {
        await using var harness = new Harness(maxRuns: 2);

        var first = harness.Runs.TrySubmit(Harness.ValidScript, string.Empty, TimeSpan.FromSeconds(30));
        Assert.NotNull(first);
        await first.TerminalTask.WaitAsync(TimeSpan.FromSeconds(10));
        harness.Time.Advance(TimeSpan.FromSeconds(1));

        var second = harness.Runs.TrySubmit(Harness.ValidScript, string.Empty, TimeSpan.FromSeconds(30));
        Assert.NotNull(second);
        await second.TerminalTask.WaitAsync(TimeSpan.FromSeconds(10));
        harness.Time.Advance(TimeSpan.FromSeconds(1));

        var third = harness.Runs.TrySubmit(Harness.ValidScript, string.Empty, TimeSpan.FromSeconds(30));
        Assert.NotNull(third);
        await third.TerminalTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(harness.Runs.Get(first.Id));
        Assert.NotNull(harness.Runs.Get(second.Id));
        Assert.NotNull(harness.Runs.Get(third.Id));
    }

    [Fact]
    public async Task WaitForTerminalAsyncReturnsAnEarlySnapshotOnTimeout()
    {
        var executor = new FakeSandboxExecutor { Block = true };
        await using var harness = new Harness(executor: executor);

        var record = harness.Runs.TrySubmit(Harness.ValidScript, string.Empty, TimeSpan.FromSeconds(30));
        Assert.NotNull(record);

        var snapshot = await RunManager.WaitForTerminalAsync(
            record,
            TimeSpan.FromMilliseconds(30),
            CancellationToken.None);

        Assert.True(snapshot.Status is RunStatus.Queued or RunStatus.Running);
        harness.Runs.Cancel(record.Id);
        await record.TerminalTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitForTerminalAsyncReturnsTheTerminalSnapshot()
    {
        await using var harness = new Harness();

        var record = harness.Runs.TrySubmit(Harness.ValidScript, string.Empty, TimeSpan.FromSeconds(30));
        Assert.NotNull(record);

        var snapshot = await RunManager.WaitForTerminalAsync(
            record,
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        Assert.Equal(RunStatus.Succeeded, snapshot.Status);
        Assert.Equal("ok", snapshot.Result!.Summary);
    }

    [Fact]
    public async Task DisposeCancelsActiveRuns()
    {
        var executor = new FakeSandboxExecutor { Block = true };
        var manager = new RunManager(
            new ScriptCompiler(),
            executor,
            new ExecutionGate(1),
            NullLogger<RunManager>.Instance,
            new FakeTimeProvider(DateTimeOffset.UnixEpoch));

        var record = manager.TrySubmit(Harness.ValidScript, string.Empty, TimeSpan.FromSeconds(30));
        Assert.NotNull(record);
        await executor.Started.WaitAsync(TimeSpan.FromSeconds(5));

        await manager.DisposeAsync();

        Assert.True(record.IsTerminal);
        Assert.Equal(RunStatus.Cancelled, record.Snapshot().Status);
    }

    [Fact]
    public async Task EveryRunGetsADistinctOpaqueIdentifier()
    {
        await using var harness = new Harness();

        var first = harness.Runs.TrySubmit(Harness.ValidScript, string.Empty, TimeSpan.FromSeconds(30));
        var second = harness.Runs.TrySubmit(Harness.ValidScript, string.Empty, TimeSpan.FromSeconds(30));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Matches("^run_[0-9a-f]{16}$", first.Id);

        await first.TerminalTask.WaitAsync(TimeSpan.FromSeconds(10));
        await second.TerminalTask.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
