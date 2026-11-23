namespace Runesgalder.Mcp;

internal static class RunStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string TimedOut = "timed_out";

    public static bool IsTerminal(string status) =>
        status is Succeeded or Failed or Cancelled or TimedOut;
}

internal sealed class RunRecord : IDisposable
{
    private readonly object _sync = new();
    private readonly TaskCompletionSource _terminal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _status = RunStatus.Queued;
    private ResultDto? _result;
    private IReadOnlyList<DiagnosticDto>? _diagnostics;
    private ResourceDto? _resource;
    private ErrorDto? _error;
    private DateTimeOffset? _terminalAt;

    public RunRecord(string id, DateTimeOffset createdAt, ExecutionGate.ExecutionSlot slot)
    {
        Id = id;
        CreatedAt = createdAt;
        Slot = slot;
    }

    public string Id { get; }

    public DateTimeOffset CreatedAt { get; }

    public ExecutionGate.ExecutionSlot Slot { get; }

    public CancellationTokenSource Cancellation { get; } = new();

    public Task TerminalTask => _terminal.Task;

    public bool IsTerminal
    {
        get
        {
            lock (_sync)
            {
                return RunStatus.IsTerminal(_status);
            }
        }
    }

    public DateTimeOffset? TerminalAt
    {
        get
        {
            lock (_sync)
            {
                return _terminalAt;
            }
        }
    }

    public bool MarkRunning()
    {
        lock (_sync)
        {
            if (RunStatus.IsTerminal(_status))
            {
                return false;
            }

            _status = RunStatus.Running;
            return true;
        }
    }

    public bool TryCancelImmediately(DateTimeOffset now)
    {
        CancellationTokenSource? cancellation = null;
        var queued = false;
        lock (_sync)
        {
            if (RunStatus.IsTerminal(_status))
            {
                return false;
            }

            queued = _status == RunStatus.Queued;
            cancellation = Cancellation;
        }

        if (queued)
        {
            Complete(
                RunStatus.Cancelled,
                result: null,
                diagnostics: null,
                resource: null,
                new ErrorDto(ErrorCodes.Cancelled, "The run was cancelled."),
                now);
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Completion already disposed the token source.
        }

        return true;
    }

    public bool Complete(
        string status,
        ResultDto? result,
        IReadOnlyList<DiagnosticDto>? diagnostics,
        ResourceDto? resource,
        ErrorDto? error,
        DateTimeOffset now)
    {
        lock (_sync)
        {
            if (RunStatus.IsTerminal(_status))
            {
                return false;
            }

            _status = status;
            _result = result;
            _diagnostics = diagnostics;
            _resource = resource;
            _error = error;
            _terminalAt = now;
        }

        _terminal.TrySetResult();
        return true;
    }

    public RunEnvelopeDto Snapshot()
    {
        lock (_sync)
        {
            return new RunEnvelopeDto(
                Id,
                _status,
                _result,
                _diagnostics,
                _resource,
                _error);
        }
    }

    public void Dispose() => Cancellation.Dispose();
}
