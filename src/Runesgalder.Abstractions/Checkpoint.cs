namespace Runesgalder;

/// <summary>A durable record of a completed flow prefix, keyed by an idempotency key.</summary>
/// <param name="Key">The idempotency key that scopes the checkpoint.</param>
/// <param name="FlowName">The name of the flow that produced the output.</param>
/// <param name="OutputType">The full name of the output type.</param>
/// <param name="OutputJson">The JSON-encoded output.</param>
/// <param name="CreatedAt">The UTC timestamp when the checkpoint was first stored.</param>
public sealed record Checkpoint(
    string Key,
    string FlowName,
    string OutputType,
    string OutputJson,
    DateTimeOffset CreatedAt);

/// <summary>Stores checkpoints so that interrupted runs can resume without repeating effects.</summary>
public interface ICheckpointStore
{
    /// <summary>Finds the checkpoint for an idempotency key.</summary>
    /// <param name="key">The idempotency key.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The checkpoint, or null when none is stored.</returns>
    ValueTask<Checkpoint?> FindAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores a checkpoint; an existing checkpoint for the same key is never overwritten.</summary>
    /// <param name="checkpoint">The checkpoint to store.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the checkpoint is durable.</returns>
    ValueTask SaveAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default);
}
