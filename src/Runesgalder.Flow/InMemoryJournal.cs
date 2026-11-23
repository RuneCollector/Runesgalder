using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Runesgalder;

/// <summary>An in-memory, append-only journal for tests and local runs.</summary>
public sealed class InMemoryJournal : IJournal
{
    private readonly ConcurrentQueue<RunEvent> _events = new();

    /// <inheritdoc />
    public ValueTask AppendAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runEvent);
        cancellationToken.ThrowIfCancellationRequested();
        _events.Enqueue(runEvent);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RunEvent> ReadAsync(
        RunId runId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var runEvent in _events.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (runEvent.RunId == runId)
            {
                yield return runEvent;
            }

            await Task.Yield();
        }
    }

    /// <summary>Returns all events currently in the journal.</summary>
    /// <returns>A snapshot of every recorded event.</returns>
    public IReadOnlyList<RunEvent> Snapshot() => _events.ToArray();
}
