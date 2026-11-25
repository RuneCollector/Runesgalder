using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Runesgalder.Sandbox;
using Runesgalder.Sandbox.Compilation;

namespace Runesgalder.Mcp;

internal sealed partial class RunManager(
    ScriptCompiler compiler,
    ISandboxExecutor executor,
    ExecutionGate gate,
    ILogger<RunManager> logger,
    TimeProvider timeProvider,
    int maxRuns = McpLimits.MaxRuns,
    TimeSpan? retention = null) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions WorkerJson = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, RunRecord> _records = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private readonly TimeSpan _retention = retention ?? McpLimits.Retention;

    public RunRecord? TrySubmit(string code, string dataCsv, TimeSpan wallClock)
    {
        if (!gate.TryReserve(out var slot) || slot is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var id = "run_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var record = new RunRecord(id, now, slot);

        lock (_sync)
        {
            PruneLocked(now, enforceCapacity: true);
            _records[id] = record;
        }

        _ = Task.Run(() => ExecuteAsync(record, code, dataCsv, wallClock));
        return record;
    }

    public RunEnvelopeDto? Get(string runId)
    {
        RunRecord? record;
        lock (_sync)
        {
            PruneLocked(timeProvider.GetUtcNow(), enforceCapacity: false);
            _records.TryGetValue(runId, out record);
        }

        return record?.Snapshot();
    }

    public RunEnvelopeDto? Cancel(string runId)
    {
        RunRecord? record;
        lock (_sync)
        {
            PruneLocked(timeProvider.GetUtcNow(), enforceCapacity: false);
            _records.TryGetValue(runId, out record);
        }

        if (record is null)
        {
            return null;
        }

        record.TryCancelImmediately(timeProvider.GetUtcNow());
        return record.Snapshot();
    }

    public static async Task<RunEnvelopeDto> WaitForTerminalAsync(
        RunRecord record,
        TimeSpan wait,
        CancellationToken cancellationToken)
    {
        if (wait <= TimeSpan.Zero)
        {
            return record.Snapshot();
        }

        try
        {
            await record.TerminalTask.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The client asked for an early envelope; the run keeps going for get_run.
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller stopped waiting; the run is not cancelled by this token.
        }

        return record.Snapshot();
    }

    private async Task ExecuteAsync(
        RunRecord record,
        string code,
        string dataCsv,
        TimeSpan wallClock)
    {
        using var activity = SandboxDiagnostics.ActivitySource.StartActivity(
            SandboxDiagnostics.RunSpanName,
            ActivityKind.Internal);
        activity?.SetTag(SandboxDiagnostics.RunIdTag, record.Id);
        activity?.SetTag(SandboxDiagnostics.SandboxBackendTag, SandboxDiagnostics.ContainerBackend);
        activity?.SetTag(SandboxDiagnostics.SandboxLimitWallTag, wallClock.TotalSeconds);
        activity?.SetTag(SandboxDiagnostics.SandboxLimitCpuTag, McpLimits.MaxCpuCores);
        activity?.SetTag(SandboxDiagnostics.SandboxLimitMemoryTag, McpLimits.MaxMemoryBytes);
        activity?.SetTag(SandboxDiagnostics.SandboxLimitPidsTag, McpLimits.MaxProcesses);
        activity?.SetTag(SandboxDiagnostics.SandboxLimitOutputTag, McpLimits.MaxOutputBytes);

        var compileMs = 0.0;
        int? exitCode = null;
        var resultWritten = false;
        var containerStarted = false;
        try
        {
            await record.Slot.EnterAsync(record.Cancellation.Token).ConfigureAwait(false);

            var queueWait = timeProvider.GetUtcNow() - record.CreatedAt;
            SandboxMetrics.RecordQueueWait(queueWait < TimeSpan.Zero ? TimeSpan.Zero : queueWait);

            if (!record.MarkRunning())
            {
                return;
            }

            var compileWatch = Stopwatch.StartNew();
            var compilation = compiler.Compile(code, record.Cancellation.Token);
            compileMs = compileWatch.Elapsed.TotalMilliseconds;
            var diagnostics = DiagnosticMapper.Map(compilation.Diagnostics);

            if (!compilation.Success)
            {
                Complete(
                    record,
                    RunStatus.Failed,
                    result: null,
                    diagnostics,
                    new ResourceDto(compileMs, 0, WallMs(record), 0),
                    DiagnosticMapper.MapCompileFailure(compilation));
                return;
            }

            if (record.Cancellation.IsCancellationRequested)
            {
                Complete(record, RunStatus.Cancelled, null, diagnostics, null, CancelledError());
                return;
            }

            containerStarted = true;
            var outcome = await executor
                .ExecuteAsync(compilation.Assembly!, dataCsv, wallClock, record.Cancellation.Token)
                .ConfigureAwait(false);
            exitCode = outcome.ExitCode;
            resultWritten = outcome.ResultWritten;

            var sandboxMs = 0.0;
            WorkerPayload? payload = null;
            if (outcome.ResultJson is not null)
            {
                try
                {
                    payload = JsonSerializer.Deserialize<WorkerPayload>(outcome.ResultJson, WorkerJson);
                }
                catch (JsonException)
                {
                    payload = null;
                }
            }

            if (payload is not null)
            {
                sandboxMs = payload.DurationMs;
            }

            var resource = new ResourceDto(compileMs, sandboxMs, WallMs(record), outcome.OutputBytes);

            if (outcome.TimedOut)
            {
                Complete(
                    record,
                    RunStatus.TimedOut,
                    result: null,
                    diagnostics,
                    resource,
                    new ErrorDto(ErrorCodes.RunTimeout, "The analysis exceeded its wall-clock limit."));
                return;
            }

            if (!outcome.ResultWritten || payload is null)
            {
                Complete(
                    record,
                    RunStatus.Failed,
                    result: null,
                    diagnostics,
                    resource,
                    new ErrorDto(
                        ErrorCodes.SandboxUnavailable,
                        "The sandbox did not produce a result. Check that Docker and the worker image are available."));
                return;
            }

            if (payload.Success)
            {
                Complete(
                    record,
                    RunStatus.Succeeded,
                    ToResult(payload),
                    diagnostics,
                    resource,
                    error: null);
                return;
            }

            var (status, error) = MapWorkerFailure(payload);
            Complete(record, status, result: null, diagnostics, resource, error);
        }
        catch (OperationCanceledException) when (record.Cancellation.IsCancellationRequested)
        {
            Complete(record, RunStatus.Cancelled, null, null, null, CancelledError());
        }
        catch (Win32Exception)
        {
            LogContainerStartFailure(logger, record.Id);
            Complete(
                record,
                RunStatus.Failed,
                null,
                null,
                new ResourceDto(compileMs, 0, WallMs(record), 0),
                new ErrorDto(
                    ErrorCodes.SandboxUnavailable,
                    "The sandbox is unavailable. Check that Docker is running and the worker image exists."));
        }
        catch (Exception exception)
        {
            LogRunFailure(logger, record.Id, exception.GetType().Name);
            Complete(
                record,
                RunStatus.Failed,
                null,
                null,
                new ResourceDto(compileMs, 0, WallMs(record), 0),
                new ErrorDto(ErrorCodes.Internal, "The run failed inside the server."));
        }
        finally
        {
            record.Slot.Dispose();
            InstrumentTerminal(activity, record, exitCode, resultWritten, containerStarted);
        }
    }

    private void InstrumentTerminal(
        Activity? activity,
        RunRecord record,
        int? exitCode,
        bool resultWritten,
        bool containerStarted)
    {
        var snapshot = record.Snapshot();
        if (!RunStatus.IsTerminal(snapshot.Status))
        {
            return;
        }

        var outcome = OutcomeOf(snapshot);
        var terminalAt = record.TerminalAt ?? timeProvider.GetUtcNow();
        var wall = terminalAt - record.CreatedAt;
        if (wall < TimeSpan.Zero)
        {
            wall = TimeSpan.Zero;
        }

        var outputBytes = snapshot.Resource?.OutputBytes ?? 0;
        var denied = outcome is "policy_denied" or "source_too_large" or "data_too_large";
        activity?.SetTag(
            SandboxDiagnostics.PolicyDecisionTag,
            denied ? SandboxDiagnostics.DenyDecision : SandboxDiagnostics.AllowDecision);
        if (denied)
        {
            activity?.SetTag(SandboxDiagnostics.PolicyReasonTag, outcome);
        }

        activity?.SetTag(SandboxDiagnostics.SandboxOutcomeTag, outcome);
        if (exitCode is not null)
        {
            activity?.SetTag(SandboxDiagnostics.SandboxExitCodeTag, exitCode.Value);
        }

        activity?.SetTag(SandboxDiagnostics.SandboxResultWrittenTag, resultWritten);
        if (containerStarted && outcome is "run_timeout" or "cancelled")
        {
            activity?.SetTag(
                SandboxDiagnostics.SandboxKillReasonTag,
                outcome == "run_timeout" ? "wall" : "cancelled");
        }

        activity?.SetTag(SandboxDiagnostics.SandboxUsedWallTag, wall.TotalSeconds);
        if (resultWritten)
        {
            activity?.SetTag(SandboxDiagnostics.SandboxUsedOutputTag, outputBytes);
        }

        if (outcome != "ok")
        {
            activity?.SetStatus(ActivityStatusCode.Error);
        }

        SandboxMetrics.RecordRun(outcome, wall);
        if (resultWritten)
        {
            SandboxMetrics.RecordOutputBytes(outputBytes, outcome);
        }
    }

    private static string OutcomeOf(RunEnvelopeDto snapshot) => snapshot.Status switch
    {
        RunStatus.Succeeded => "ok",
        RunStatus.TimedOut => "run_timeout",
        RunStatus.Cancelled => "cancelled",
        _ => snapshot.Error?.Code ?? "internal"
    };

    private void Complete(
        RunRecord record,
        string status,
        ResultDto? result,
        IReadOnlyList<DiagnosticDto>? diagnostics,
        ResourceDto? resource,
        ErrorDto? error) =>
        record.Complete(status, result, diagnostics, resource, error, timeProvider.GetUtcNow());

    private double WallMs(RunRecord record) =>
        Math.Max(0, (timeProvider.GetUtcNow() - record.CreatedAt).TotalMilliseconds);

    private static ResultDto ToResult(WorkerPayload payload)
    {
        var charts = (payload.Charts ?? [])
            .Select(chart => new ChartDto(
                (chart.Kind ?? "bar").ToLowerInvariant(),
                chart.Title ?? string.Empty,
                chart.Labels ?? [],
                chart.Values ?? []))
            .ToArray();

        return new ResultDto(payload.Summary ?? string.Empty, charts, payload.Logs ?? []);
    }

    private static (string Status, ErrorDto Error) MapWorkerFailure(WorkerPayload payload)
    {
        var message = string.IsNullOrWhiteSpace(payload.Message) ? "The analysis failed." : payload.Message;
        return payload.Error switch
        {
            "timeout" => (RunStatus.TimedOut, new ErrorDto(ErrorCodes.RunTimeout, message)),
            "output-quota" => (RunStatus.Failed, new ErrorDto(ErrorCodes.PolicyDenied, message)),
            "input" => (RunStatus.Failed, new ErrorDto(
                message.Contains("data rows", StringComparison.OrdinalIgnoreCase)
                    ? ErrorCodes.DataTooLarge
                    : ErrorCodes.InvalidRequest,
                message)),
            "contract" => (RunStatus.Failed, new ErrorDto(ErrorCodes.ContractInvalid, message)),
            // The v0.1 error list has no runtime-script code; keep the worker message for the client.
            "script" => (RunStatus.Failed, new ErrorDto(ErrorCodes.Internal, message)),
            _ => (RunStatus.Failed, new ErrorDto(ErrorCodes.Internal, message))
        };
    }

    private static ErrorDto CancelledError() =>
        new(ErrorCodes.Cancelled, "The run was cancelled.");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Run {RunId} could not start the container.")]
    private static partial void LogContainerStartFailure(ILogger logger, string runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Run {RunId} failed with {ExceptionType}.")]
    private static partial void LogRunFailure(ILogger logger, string runId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Timed out waiting for active runs during shutdown.")]
    private static partial void LogShutdownTimeout(ILogger logger);

    private void PruneLocked(DateTimeOffset now, bool enforceCapacity)
    {
        var expired = _records
            .Where(pair => pair.Value.IsTerminal &&
                pair.Value.TerminalAt is { } terminalAt &&
                terminalAt <= now - _retention)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var id in expired)
        {
            RemoveLocked(id);
        }

        if (!enforceCapacity)
        {
            return;
        }

        while (_records.Count >= maxRuns)
        {
            var oldest = _records
                .Where(pair => pair.Value.IsTerminal)
                .OrderBy(pair => pair.Value.TerminalAt)
                .FirstOrDefault();

            if (oldest.Key is null)
            {
                break;
            }

            RemoveLocked(oldest.Key);
        }
    }

    private void RemoveLocked(string id)
    {
        if (_records.Remove(id, out var record))
        {
            record.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        RunRecord[] records;
        lock (_sync)
        {
            records = _records.Values.ToArray();
        }

        var now = timeProvider.GetUtcNow();
        foreach (var record in records)
        {
            record.TryCancelImmediately(now);
        }

        try
        {
            await Task.WhenAll(records.Select(record => record.TerminalTask))
                .WaitAsync(TimeSpan.FromSeconds(7))
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            LogShutdownTimeout(logger);
        }

        lock (_sync)
        {
            foreach (var id in _records.Keys.ToArray())
            {
                RemoveLocked(id);
            }
        }
    }

    private sealed record WorkerPayload(
        bool Success,
        string? Error,
        string? Message,
        string? Summary,
        IReadOnlyList<WorkerChart>? Charts,
        IReadOnlyList<string>? Logs,
        double DurationMs);

    private sealed record WorkerChart(
        string? Kind,
        string? Title,
        IReadOnlyList<string>? Labels,
        IReadOnlyList<double>? Values);
}
