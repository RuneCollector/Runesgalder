namespace Runesgalder.Mcp;

internal sealed class ExecutionGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _capacity;
    private int _reserved;

    public ExecutionGate(int capacity = McpLimits.QueueCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public bool TryReserve(out ExecutionSlot? slot)
    {
        while (true)
        {
            var current = Volatile.Read(ref _reserved);
            if (current >= _capacity)
            {
                slot = null;
                return false;
            }

            if (Interlocked.CompareExchange(ref _reserved, current + 1, current) == current)
            {
                slot = new ExecutionSlot(this);
                return true;
            }
        }
    }

    private async ValueTask EnterAsync(CancellationToken cancellationToken) =>
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

    private void Release(bool acquired)
    {
        if (acquired)
        {
            _gate.Release();
        }

        Interlocked.Decrement(ref _reserved);
    }

    public void Dispose() => _gate.Dispose();

    internal sealed class ExecutionSlot : IDisposable
    {
        private readonly ExecutionGate _gate;
        private bool _acquired;
        private int _disposed;

        public ExecutionSlot(ExecutionGate gate) => _gate = gate;

        public async ValueTask EnterAsync(CancellationToken cancellationToken)
        {
            await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
            _acquired = true;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _gate.Release(_acquired);
            }
        }
    }
}
