using System.Globalization;

namespace Runesgalder.FlowTests;

public sealed class JournalConcurrencyTests
{
    private const int Writers = 4;
    private const int EventsPerWriter = 25;
    private static readonly DateTimeOffset At = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    private static readonly RunId[] Runs =
    [
        new(Guid.Parse("11111111-1111-1111-1111-111111111111")),
        new(Guid.Parse("22222222-2222-2222-2222-222222222222")),
        new(Guid.Parse("33333333-3333-3333-3333-333333333333")),
        new(Guid.Parse("44444444-4444-4444-4444-444444444444"))
    ];

    public static TheoryData<string> Journals => new() { "memory", "sqlite" };

    [Theory]
    [MemberData(nameof(Journals))]
    public async Task PreservesEveryEventWhenWritersAppendConcurrently(string kind)
    {
        using var workspace = new TempDirectory();
        await using var sqlite = kind == "sqlite"
            ? new SqliteJournal(Path.Combine(workspace.Root, "concurrent.db"))
            : null;
        IJournal journal = sqlite is null ? new InMemoryJournal() : sqlite;

        await Task.WhenAll(Enumerable.Range(0, Writers).Select(writer => WriteRunAsync(journal, writer)));

        foreach (var writer in Enumerable.Range(0, Writers))
        {
            Assert.Equal(ExpectedStepNames(writer), await ReadStepNamesAsync(journal, Runs[writer]));
        }
    }

    [Theory]
    [MemberData(nameof(Journals))]
    public async Task KeepsTheFirstCheckpointWhenWritersRaceForOneKey(string kind)
    {
        using var workspace = new TempDirectory();
        await using var sqlite = kind == "sqlite"
            ? new SqliteJournal(Path.Combine(workspace.Root, "race.db"))
            : null;
        ICheckpointStore store = sqlite is null ? new InMemoryCheckpointStore() : sqlite;

        await Task.WhenAll(Enumerable.Range(0, 20).Select(index => Task.Run(async () =>
            await store.SaveAsync(new Checkpoint(
                "shared",
                "flow",
                typeof(int).FullName!,
                index.ToString(CultureInfo.InvariantCulture),
                At.AddMilliseconds(index))))));

        var found = await store.FindAsync("shared");
        Assert.NotNull(found);
        Assert.InRange(int.Parse(found.OutputJson, CultureInfo.InvariantCulture), 0, 19);
        Assert.Equal(found.OutputJson, (await store.FindAsync("shared"))!.OutputJson);
    }

    private static async Task WriteRunAsync(IJournal journal, int writer)
    {
        for (var index = 0; index < EventsPerWriter; index++)
        {
            var step = $"w{writer}-{index}";
            await journal.AppendAsync(new StepStarted(Runs[writer], StepId.FromName(step), step, At));
        }
    }

    private static string[] ExpectedStepNames(int writer) =>
        Enumerable.Range(0, EventsPerWriter).Select(index => $"w{writer}-{index}").ToArray();

    private static async Task<string[]> ReadStepNamesAsync(IJournal journal, RunId runId)
    {
        var names = new List<string>();
        await foreach (var runEvent in journal.ReadAsync(runId))
        {
            names.Add(Assert.IsType<StepStarted>(runEvent).StepName);
        }

        return names.ToArray();
    }
}
