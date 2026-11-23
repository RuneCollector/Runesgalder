using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Runesgalder.Sandbox.Compilation;

namespace Runesgalder.Mcp;

internal sealed partial class SandboxTools(
    ScriptCompiler compiler,
    ExecutionGate gate,
    RunManager runs,
    McpOptions options,
    ILogger<SandboxTools> logger)
{
    public static IList<Tool> ListTools() => ToolCatalog.Create();

    public async Task<CallToolResult> CallAsync(
        string name,
        IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken)
    {
        var args = arguments ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        return name switch
        {
            "compile_csharp" => await CompileAsync(args, cancellationToken).ConfigureAwait(false),
            "run_csharp" => await RunAsync(args, cancellationToken).ConfigureAwait(false),
            "get_run" => GetRun(args),
            "cancel_run" => CancelRun(args),
            _ => Result(
                new ErrorDto(ErrorCodes.InvalidRequest, $"Unknown tool '{name}'."),
                isError: true)
        };
    }

    private async Task<CallToolResult> CompileAsync(
        IDictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken)
    {
        CompileResponseDto response;
        try
        {
            var request = ParseCompile(arguments);
            if (!gate.TryReserve(out var slot) || slot is null)
            {
                response = CompileFailure(
                    ErrorCodes.ServerBusy,
                    "The server is busy; retry shortly.",
                    durationMs: 0);
            }
            else
            {
                try
                {
                    await slot.EnterAsync(cancellationToken).ConfigureAwait(false);
                    var watch = Stopwatch.StartNew();
                    var compilation = compiler.Compile(request.Code, cancellationToken);
                    response = ToCompileResponse(compilation, watch.Elapsed.TotalMilliseconds);
                }
                finally
                {
                    slot.Dispose();
                }
            }
        }
        catch (ToolInputException exception)
        {
            response = CompileFailure(exception.Code, exception.Message, durationMs: 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            response = CompileFailure(ErrorCodes.Cancelled, "The compile was cancelled.", durationMs: 0);
        }
        catch (Exception exception)
        {
            LogCompileFailure(logger, exception.GetType().Name);
            response = CompileFailure(
                ErrorCodes.Internal,
                "The compile failed inside the server.",
                durationMs: 0);
        }

        return Result(response, isError: !response.Ok);
    }

    private async Task<CallToolResult> RunAsync(
        IDictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken)
    {
        RunEnvelopeDto response;
        try
        {
            var request = ParseRun(arguments);
            var record = runs.TrySubmit(request.Code, request.DataCsv, request.WallClock);
            response = record is null
                ? ErrorEnvelope(
                    ErrorCodes.ServerBusy,
                    "The server is busy; retry shortly.")
                : await RunManager
                    .WaitForTerminalAsync(record, request.Wait, cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (ToolInputException exception)
        {
            response = ErrorEnvelope(exception.Code, exception.Message);
        }
        catch (Exception exception)
        {
            LogRunFailure(logger, exception.GetType().Name);
            response = ErrorEnvelope(ErrorCodes.Internal, "The run failed inside the server.");
        }

        return Result(response, IsRunError(response));
    }

    private CallToolResult GetRun(IDictionary<string, JsonElement> arguments)
    {
        RunEnvelopeDto response;
        try
        {
            var runId = ParseRunId(arguments);
            response = runs.Get(runId) ?? new RunEnvelopeDto(
                runId,
                RunStatus.Failed,
                Result: null,
                Diagnostics: null,
                Resource: null,
                Error: new ErrorDto(ErrorCodes.RunNotFound, "The run was not found or has expired."));
        }
        catch (ToolInputException exception)
        {
            response = ErrorEnvelope(exception.Code, exception.Message);
        }

        return Result(response, IsRunError(response));
    }

    private CallToolResult CancelRun(IDictionary<string, JsonElement> arguments)
    {
        CancelResponseDto response;
        try
        {
            var runId = ParseRunId(arguments);
            var envelope = runs.Cancel(runId);
            response = envelope is null
                ? new CancelResponseDto(
                    runId,
                    RunStatus.Failed,
                    new ErrorDto(ErrorCodes.RunNotFound, "The run was not found or has expired."))
                : new CancelResponseDto(envelope.RunId, envelope.Status, Error: null);
        }
        catch (ToolInputException exception)
        {
            response = new CancelResponseDto(
                RunId: null,
                RunStatus.Failed,
                new ErrorDto(exception.Code, exception.Message));
        }

        return Result(response, isError: response.Error is not null);
    }

    private static CompileRequest ParseCompile(IDictionary<string, JsonElement> arguments)
    {
        ToolArguments.RejectUnknown(arguments, "code", "contract");
        var code = ToolArguments.RequiredString(arguments, "code");
        var contract = ToolArguments.OptionalString(arguments, "contract");
        if (contract is not null &&
            !string.Equals(contract, McpOptions.AnalysisContract, StringComparison.Ordinal))
        {
            throw new ToolInputException(
                ErrorCodes.ContractInvalid,
                "Only the analysis contract is supported in v0.1.");
        }

        EnsureSourceSize(code);
        return new CompileRequest(code);
    }

    private RunRequest ParseRun(IDictionary<string, JsonElement> arguments)
    {
        ToolArguments.RejectUnknown(
            arguments,
            "code",
            "data",
            "wait_seconds",
            "wall_clock_seconds");

        var code = ToolArguments.RequiredString(arguments, "code");
        EnsureSourceSize(code);
        var dataCsv = ParseData(arguments);

        var waitSeconds = ToolArguments.OptionalInteger(arguments, "wait_seconds") ??
            McpLimits.DefaultWaitSeconds;
        if (waitSeconds < 0 || waitSeconds > McpLimits.MaxWaitSeconds)
        {
            throw new ToolInputException(
                ErrorCodes.InvalidRequest,
                $"The field 'wait_seconds' must be between 0 and {McpLimits.MaxWaitSeconds}.");
        }

        var wallClockSeconds = ToolArguments.OptionalNumber(arguments, "wall_clock_seconds") ??
            options.DefaultWallClock.TotalSeconds;
        var maxWallClock = options.MaxWallClock.TotalSeconds;
        if (wallClockSeconds <= 0 || wallClockSeconds > maxWallClock)
        {
            throw new ToolInputException(
                ErrorCodes.InvalidRequest,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The field 'wall_clock_seconds' must be greater than zero and at most {maxWallClock}."));
        }

        return new RunRequest(
            code,
            dataCsv,
            TimeSpan.FromSeconds(waitSeconds),
            TimeSpan.FromSeconds(wallClockSeconds));
    }

    private static string ParseData(IDictionary<string, JsonElement> arguments)
    {
        if (!arguments.TryGetValue("data", out var dataElement))
        {
            return string.Empty;
        }

        var data = ToolArguments.ReadObject(dataElement, "data");
        ToolArguments.RejectUnknown(data, "name", "csv");

        var csv = ToolArguments.RequiredString(data, "csv");
        if (Encoding.UTF8.GetByteCount(csv) > McpLimits.MaxDataBytes)
        {
            throw new ToolInputException(
                ErrorCodes.DataTooLarge,
                $"The data is larger than the {McpLimits.MaxDataBytes} byte limit.");
        }

        var name = ToolArguments.OptionalString(data, "name");
        if (name is not null)
        {
            if (name.Length > 128)
            {
                throw new ToolInputException(
                    ErrorCodes.InvalidRequest,
                    "The field 'data.name' must be at most 128 characters.");
            }

            if (name.IndexOfAny(['/', '\\', ':']) >= 0 || name.Any(char.IsControl))
            {
                throw new ToolInputException(
                    ErrorCodes.InvalidRequest,
                    "The field 'data.name' must be a table label, not a path.");
            }
        }

        return csv;
    }

    private static string ParseRunId(IDictionary<string, JsonElement> arguments)
    {
        ToolArguments.RejectUnknown(arguments, "run_id");
        var runId = ToolArguments.RequiredString(arguments, "run_id");
        if (string.IsNullOrWhiteSpace(runId) || runId.Length > 128)
        {
            throw new ToolInputException(
                ErrorCodes.InvalidRequest,
                "The field 'run_id' must be a non-empty run identifier.");
        }

        return runId;
    }

    private static void EnsureSourceSize(string code)
    {
        if (Encoding.UTF8.GetByteCount(code) > McpLimits.MaxSourceBytes)
        {
            throw new ToolInputException(
                ErrorCodes.SourceTooLarge,
                $"The script is larger than the {McpLimits.MaxSourceBytes} byte limit.");
        }
    }

    private static CompileResponseDto ToCompileResponse(
        ScriptCompilationResult compilation,
        double durationMs)
    {
        var diagnostics = DiagnosticMapper.Map(compilation.Diagnostics);
        if (compilation.Success)
        {
            return new CompileResponseDto(
                Ok: true,
                diagnostics,
                new ArtifactDto(compilation.AssemblyHash!, compilation.Assembly!.Length),
                durationMs,
                Error: null);
        }

        return new CompileResponseDto(
            Ok: false,
            diagnostics,
            Artifact: null,
            durationMs,
            DiagnosticMapper.MapCompileFailure(compilation));
    }

    private static CompileResponseDto CompileFailure(string code, string message, double durationMs) =>
        new(
            Ok: false,
            Diagnostics: [],
            Artifact: null,
            durationMs,
            new ErrorDto(code, message));

    private static RunEnvelopeDto ErrorEnvelope(string code, string message) =>
        new(
            RunId: null,
            RunStatus.Failed,
            Result: null,
            Diagnostics: null,
            Resource: null,
            new ErrorDto(code, message));

    private static bool IsRunError(RunEnvelopeDto envelope) =>
        envelope.Error is not null ||
        envelope.Status is RunStatus.Failed or RunStatus.Cancelled or RunStatus.TimedOut;

    private static CallToolResult Result(object payload, bool isError) =>
        new()
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(payload, payload.GetType(), McpJson.Options)
                }
            ],
            StructuredContent = JsonSerializer.SerializeToElement(
                payload,
                payload.GetType(),
                McpJson.Options),
            IsError = isError
        };

    [LoggerMessage(Level = LogLevel.Error, Message = "compile_csharp failed with {ExceptionType}.")]
    private static partial void LogCompileFailure(ILogger logger, string exceptionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "run_csharp failed with {ExceptionType}.")]
    private static partial void LogRunFailure(ILogger logger, string exceptionType);

    private sealed record CompileRequest(string Code);

    private sealed record RunRequest(
        string Code,
        string DataCsv,
        TimeSpan Wait,
        TimeSpan WallClock);
}
