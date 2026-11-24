using Microsoft.Data.Sqlite;

namespace Runesgalder.FlowTests;

public sealed class SqliteJournalTests
{
    private static readonly RunId SampleRun = new(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"));
    private static readonly DateTimeOffset At = new(2026, 11, 23, 10, 30, 0, TimeSpan.FromHours(1));

    public static TheoryData<RunEvent> EventSamples => new()
    {
        new RunStarted(SampleRun, "demo", At),
        new StepStarted(SampleRun, StepId.FromName("plan"), "plan", At),
        new StepCompleted(SampleRun, StepId.FromName("plan"), "plan", TimeSpan.FromMilliseconds(12), At),
        new StepFailed(SampleRun, StepId.FromName("plan"), "plan", ErrorCodes.FlowStepFailed, "boom", At),
        new StepRetryScheduled(SampleRun, StepId.FromName("plan"), 2, ErrorCodes.FlowStepFailed, "boom", At),
        new ModelCallStarted(SampleRun, StepId.FromName("plan"), "plan", "fake-model", At),
        new ModelCallCompleted(SampleRun, StepId.FromName("plan"), "plan", "fake-model", 10, 20, TimeSpan.FromSeconds(1), At),
        new ModelCallStarted(SampleRun, StepId.FromName("plan"), "plan", null, At),
        new ModelCallCompleted(SampleRun, StepId.FromName("plan"), "plan", null, null, null, TimeSpan.Zero, At),
        new CheckpointStored(SampleRun, "job-1", "demo", At),
        new CheckpointRestored(SampleRun, "job-1", "demo", At),
        new RunCompleted(SampleRun, TimeSpan.FromSeconds(2), At),
        new RunFailed(SampleRun, ErrorCodes.FlowStepFailed, "boom", At)
    };

    [Theory]
    [MemberData(nameof(EventSamples))]
    public async Task RoundTripsEveryEventShape(RunEvent original)
    {
        using var workspace = new TempDirectory();
        var path = Path.Combine(workspace.Root, "runs.db");

        await using (var journal = new SqliteJournal(path))
        {
            await journal.AppendAsync(original);
        }

        await using var reopened = new SqliteJournal(path);
        var events = new List<RunEvent>();
        await foreach (var runEvent in reopened.ReadAsync(original.RunId))
        {
            events.Add(runEvent);
        }

        Assert.Equal([original], events);
    }

    [Fact]
    public async Task KeepsEventOrderAndTheRunSummaryAcrossReopens()
    {
        using var workspace = new TempDirectory();
        var path = Path.Combine(workspace.Root, "runs.db");
        var flow = Flow.Start(Step.Define<int, int>("inc", value => value + 1))
            .Then(Step.Define<int, string>("fmt", value => $"v{value}"));
        RunId runId;

        await using (var journal = new SqliteJournal(path))
        {
            var run = flow.BeginRun(1, journal);
            runId = run.RunId;
            Assert.Equal("v2", await run.Result);
            await foreach (var _ in run.Events())
            {
            }
        }

        await using var reopened = new SqliteJournal(path);
        var events = new List<RunEvent>();
        await foreach (var runEvent in reopened.ReadAsync(runId))
        {
            events.Add(runEvent);
        }

        Assert.Collection(
            events,
            e => Assert.Equal("inc -> fmt", Assert.IsType<RunStarted>(e).FlowName),
            e => Assert.Equal("inc", Assert.IsType<StepStarted>(e).StepName),
            e => Assert.Equal("inc", Assert.IsType<StepCompleted>(e).StepName),
            e => Assert.Equal("fmt", Assert.IsType<StepStarted>(e).StepName),
            e => Assert.Equal("fmt", Assert.IsType<StepCompleted>(e).StepName),
            e => Assert.IsType<RunCompleted>(e));
    }

    [Fact]
    public async Task KeepsTheFirstCheckpointForAKey()
    {
        using var workspace = new TempDirectory();
        await using var journal = new SqliteJournal(Path.Combine(workspace.Root, "runs.db"));

        await journal.SaveAsync(new Checkpoint("key", "flow", typeof(int).FullName!, "1", At));
        await journal.SaveAsync(new Checkpoint("key", "flow", typeof(int).FullName!, "2", At.AddMinutes(1)));

        var found = await journal.FindAsync("key");
        Assert.NotNull(found);
        Assert.Equal("1", found.OutputJson);
        Assert.Equal(At, found.CreatedAt);
    }

    [Fact]
    public async Task ResumesACheckpointedPrefixAfterReopeningTheJournal()
    {
        using var workspace = new TempDirectory();
        var path = Path.Combine(workspace.Root, "runs.db");
        var calls = 0;

        Flow<int, int> Build(SqliteJournal store) =>
            Flow.Start(Step.Define<int, int>(
                    "costly",
                    value =>
                    {
                        calls++;
                        return value * 10;
                    }))
                .Checkpoint(store, "job-9");

        await using (var journal = new SqliteJournal(path))
        {
            Assert.Equal(20, await Build(journal).RunAsync(2));
        }

        await using (var reopened = new SqliteJournal(path))
        {
            Assert.Equal(20, await Build(reopened).RunAsync(2));

            var run = Build(reopened).BeginRun(2, reopened);
            Assert.Equal(20, await run.Result);
            var restored = new List<CheckpointRestored>();
            await foreach (var runEvent in run.Events())
            {
                if (runEvent is CheckpointRestored value)
                {
                    restored.Add(value);
                }
            }

            var checkpoint = Assert.Single(restored);
            Assert.Equal("job-9", checkpoint.Key);
        }

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DoesNotPersistACheckpointForAFailedRun()
    {
        using var workspace = new TempDirectory();
        var path = Path.Combine(workspace.Root, "runs.db");

        await using (var journal = new SqliteJournal(path))
        {
            var flow = Flow.Start(Step.Define<int, int>(
                    "fails",
                    _ => throw new InvalidOperationException("boom")))
                .Checkpoint(journal, "failed-job");

            await Assert.ThrowsAsync<InvalidOperationException>(() => flow.RunAsync(1));
        }

        await using (var reopened = new SqliteJournal(path))
        {
            Assert.Null(await reopened.FindAsync("failed-job"));
        }
    }

    [Fact]
    public async Task PersistsAFailedRunWithoutARunCompletedEvent()
    {
        using var workspace = new TempDirectory();
        var path = Path.Combine(workspace.Root, "runs.db");
        var flow = Flow.Start(Step.Define<int, int>(
            "boom",
            _ => throw new InvalidOperationException("boom")));
        RunId runId;

        await using (var journal = new SqliteJournal(path))
        {
            var run = flow.BeginRun(1, journal);
            runId = run.RunId;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await run.Result);
        }

        await using var reopened = new SqliteJournal(path);
        var events = new List<RunEvent>();
        await foreach (var runEvent in reopened.ReadAsync(runId))
        {
            events.Add(runEvent);
        }

        Assert.Collection(
            events,
            e => Assert.IsType<RunStarted>(e),
            e => Assert.Equal("boom", Assert.IsType<StepStarted>(e).StepName),
            e => Assert.Equal(ErrorCodes.FlowStepFailed, Assert.IsType<StepFailed>(e).ErrorCode),
            e => Assert.Equal(ErrorCodes.FlowStepFailed, Assert.IsType<RunFailed>(e).ErrorCode));
    }

    [Fact]
    public async Task RejectsANewerSchemaVersion()
    {
        using var workspace = new TempDirectory();
        var path = Path.Combine(workspace.Root, "runs.db");

        await using (var journal = new SqliteJournal(path))
        {
            await journal.AppendAsync(new RunStarted(SampleRun, "schema", At));
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString();
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99;";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await using var newer = new SqliteJournal(path);
        });
    }
}
