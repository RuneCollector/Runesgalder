using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Runesgalder.Mcp;
using Runesgalder.Sandbox.Compilation;

namespace Runesgalder.McpTests;

internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly object _sync = new();
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
        {
            return _now;
        }
    }

    public void Advance(TimeSpan delta)
    {
        lock (_sync)
        {
            _now += delta;
        }
    }
}

internal sealed class FakeSandboxExecutor : ISandboxExecutor
{
    private static readonly string[] Labels = ["a"];
    private static readonly double[] Values = [1.0];
    private static readonly string[] LogLines = ["hello"];
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calls;

    public bool Block { get; init; }

    public SandboxOutcome Outcome { get; set; } = Success();

    public Task Started => _started.Task;

    public int Calls => Volatile.Read(ref _calls);

    public void Release() => _release.TrySetResult();

    public async Task<SandboxOutcome> ExecuteAsync(
        byte[] assembly,
        string dataCsv,
        TimeSpan wallClock,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        _started.TrySetResult();

        if (Block)
        {
            await _release.Task.WaitAsync(cancellationToken);
        }

        return Outcome;
    }

    public static SandboxOutcome Success(string summary = "ok") => new(
        TimedOut: false,
        ResultWritten: true,
        JsonSerializer.Serialize(new
        {
            success = true,
            error = (string?)null,
            message = (string?)null,
            summary,
            charts = new[]
            {
                new { kind = "Bar", title = "Rows", labels = Labels, values = Values }
            },
            logs = LogLines,
            durationMs = 12.5
        }),
        OutputBytes: 128);

    public static SandboxOutcome Failure(string error, string message) => new(
        TimedOut: false,
        ResultWritten: true,
        JsonSerializer.Serialize(new
        {
            success = false,
            error,
            message,
            summary = (string?)null,
            charts = (object?)null,
            logs = (string[]?)null,
            durationMs = 3.0
        }),
        OutputBytes: 64);
}

internal sealed class Harness : IAsyncDisposable
{
    public const string ValidScript = """
        using System.Threading.Tasks;
        using Runesgalder.Sandbox;

        public sealed class Analysis : IAnalysis
        {
            public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
            {
                context.Log(table.Name);
                return Task.FromResult(new AnalysisResult("rows", []));
            }
        }
        """;

    private static readonly DateTimeOffset Start = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    public Harness(
        int gateCapacity = 5,
        ISandboxExecutor? executor = null,
        McpOptions? options = null,
        int maxRuns = McpLimits.MaxRuns,
        FakeTimeProvider? timeProvider = null)
    {
        Time = timeProvider ?? new FakeTimeProvider(Start);
        Gate = new ExecutionGate(gateCapacity);
        Options = options ?? new McpOptions();
        Executor = executor ?? new FakeSandboxExecutor();
        var compiler = new ScriptCompiler();
        Runs = new RunManager(
            compiler,
            Executor,
            Gate,
            NullLogger<RunManager>.Instance,
            Time,
            maxRuns);
        Tools = new SandboxTools(
            compiler,
            Gate,
            Runs,
            Options,
            NullLogger<SandboxTools>.Instance);
    }

    public FakeTimeProvider Time { get; }

    public ExecutionGate Gate { get; }

    public McpOptions Options { get; }

    public ISandboxExecutor Executor { get; }

    public RunManager Runs { get; }

    public SandboxTools Tools { get; }

    public static Dictionary<string, JsonElement> Args(object value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return document.RootElement
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }

    public static JsonElement Structured(CallToolResult result) =>
        result.StructuredContent ?? throw new InvalidOperationException("The result has no structured content.");

    public static string ErrorCode(CallToolResult result)
    {
        var payload = Structured(result);
        return payload.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
            ? error.GetProperty("code").GetString()
                ?? throw new InvalidOperationException("The error has no code.")
            : payload.GetProperty("code").GetString()
                ?? throw new InvalidOperationException("The error has no code.");
    }

    public async ValueTask DisposeAsync()
    {
        await Runs.DisposeAsync();
    }
}
