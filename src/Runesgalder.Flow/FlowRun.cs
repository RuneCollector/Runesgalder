using System.Threading.Channels;

namespace Runesgalder;

/// <summary>A started flow run with a live event stream.</summary>
/// <typeparam name="TOut">The flow output type.</typeparam>
public sealed class FlowRun<TOut>
{
    private readonly ChannelReader<RunEvent> _events;

    internal FlowRun(RunId runId, ChannelReader<RunEvent> events, Task<TOut> result)
    {
        RunId = runId;
        _events = events;
        Result = result;
    }

    /// <summary>The run identifier.</summary>
    public RunId RunId { get; }

    /// <summary>Completes with the flow output, or faults with the flow exception.</summary>
    public Task<TOut> Result { get; }

    /// <summary>Streams journal events in append order until the run ends.</summary>
    /// <param name="cancellationToken">A cancellation token that stops observation only.</param>
    /// <returns>The event stream.</returns>
    public IAsyncEnumerable<RunEvent> Events(CancellationToken cancellationToken = default) =>
        _events.ReadAllAsync(cancellationToken);
}
