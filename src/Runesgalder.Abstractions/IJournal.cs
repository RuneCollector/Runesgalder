namespace Runesgalder;

/// <summary>Append-only journal for run events.</summary>
public interface IJournal
{
    /// <summary>Appends an event to the journal.</summary>
    /// <param name="runEvent">The event to append.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the event is durable.</returns>
    ValueTask AppendAsync(RunEvent runEvent, CancellationToken cancellationToken = default);

    /// <summary>Reads the events for a run in append order.</summary>
    /// <param name="runId">The run identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An asynchronous stream of events.</returns>
    IAsyncEnumerable<RunEvent> ReadAsync(RunId runId, CancellationToken cancellationToken = default);
}
