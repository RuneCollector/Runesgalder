using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Runesgalder.FlowTests;

public sealed class CheckpointTests
{
    [Fact]
    public async Task ReusesTheStoredOutputForTheSameIdempotencyKey()
    {
        var store = new InMemoryCheckpointStore();
        var calls = 0;
        var flow = Flow.Start(Step.Define<int, int>(
                "work",
                value =>
                {
                    calls++;
                    return value * 2;
                }))
            .Checkpoint(store, "job-42");

        var first = await flow.RunAsync(21);
        var secondJournal = new InMemoryJournal();
        var second = await flow.RunAsync(21, secondJournal);

        Assert.Equal(42, first);
        Assert.Equal(42, second);
        Assert.Equal(1, calls);
        var restored = Assert.Single(secondJournal.Snapshot().OfType<CheckpointRestored>());
        Assert.Equal("job-42", restored.Key);
    }

    [Fact]
    public async Task DerivesTheKeyFromTheFlowNameAndInput()
    {
        var store = new InMemoryCheckpointStore();
        var calls = 0;
        var flow = Flow.Start(Step.Define<string, string>(
                "echo",
                value =>
                {
                    calls++;
                    return value;
                }))
            .Checkpoint(store);

        await flow.RunAsync("same");
        var replayJournal = new InMemoryJournal();
        await flow.RunAsync("same", replayJournal);
        await flow.RunAsync("other");

        Assert.Equal(2, calls);
        Assert.Single(replayJournal.Snapshot().OfType<CheckpointRestored>());
        Assert.Equal(2, await CountCheckpointsAsync(store, flow.Name));
    }

    [Fact]
    public async Task CheckpointsOnlyThePrefixWhenAppliedMidFlow()
    {
        var store = new InMemoryCheckpointStore();
        var prefixCalls = 0;
        var suffixCalls = 0;
        var flow = Flow.Start(Step.Define<int, int>(
                "prefix",
                value =>
                {
                    prefixCalls++;
                    return value + 1;
                }))
            .Checkpoint(store, "job-7")
            .Then(Step.Define<int, int>(
                "suffix",
                value =>
                {
                    suffixCalls++;
                    return value * 10;
                }));

        Assert.Equal(20, await flow.RunAsync(1));
        Assert.Equal(20, await flow.RunAsync(1));

        Assert.Equal(1, prefixCalls);
        Assert.Equal(2, suffixCalls);
    }

    [Fact]
    public async Task RejectsACheckpointStoredForADifferentOutputType()
    {
        var store = new InMemoryCheckpointStore();
        await store.SaveAsync(new Checkpoint(
            "shared",
            "other-flow",
            typeof(int).FullName!,
            "42",
            DateTimeOffset.UtcNow));
        var flow = Flow.Start(Step.Define<int, string>("format", value => value.ToString(CultureInfo.InvariantCulture)))
            .Checkpoint(store, "shared");

        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.RunAsync(1));
    }

    private static async Task<int> CountCheckpointsAsync(InMemoryCheckpointStore store, string flowName)
    {
        var count = 0;
        foreach (var value in new[] { "same", "other" })
        {
            var json = JsonSerializer.Serialize(value, CheckpointJson.Options);
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            if (await store.FindAsync($"{flowName}:{hash}") is not null)
            {
                count++;
            }
        }

        return count;
    }
}
