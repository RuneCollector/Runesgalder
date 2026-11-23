using System.Collections.Concurrent;

namespace Runesgalder;

/// <summary>An in-memory checkpoint store for tests and local runs.</summary>
public sealed class InMemoryCheckpointStore : ICheckpointStore
{
    private readonly ConcurrentDictionary<string, Checkpoint> _checkpoints = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<Checkpoint?> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_checkpoints.TryGetValue(key, out var checkpoint) ? checkpoint : null);
    }

    /// <inheritdoc />
    public ValueTask SaveAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();
        _checkpoints.TryAdd(checkpoint.Key, checkpoint);
        return ValueTask.CompletedTask;
    }
}
