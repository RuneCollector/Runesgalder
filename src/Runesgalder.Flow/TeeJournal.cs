using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Runesgalder;

/// <summary>Forwards appends to an inner journal and to a live event channel.</summary>
internal sealed class TeeJournal(IJournal inner) : IJournal
{
    private readonly Channel<RunEvent> _channel = Channel.CreateUnbounded<RunEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ChannelReader<RunEvent> Events => _channel.Reader;

    public async ValueTask AppendAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runEvent);
        await inner.AppendAsync(runEvent, cancellationToken).ConfigureAwait(false);
        _channel.Writer.TryWrite(runEvent);
    }

    public async IAsyncEnumerable<RunEvent> ReadAsync(
        RunId runId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var runEvent in inner.ReadAsync(runId, cancellationToken).ConfigureAwait(false))
        {
            yield return runEvent;
        }
    }

    public void Complete() => _channel.Writer.TryComplete();
}
