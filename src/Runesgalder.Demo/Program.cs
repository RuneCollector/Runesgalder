using System.Globalization;
using System.Text.Json;
using Runesgalder;
using Runesgalder.Demo;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("RUNESGALDER_DEMO_URLS") ?? "http://127.0.0.1:5210");
builder.Services.AddSingleton(DemoOptions.FromEnvironment());
builder.Services.AddSingleton<DemoRunner>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/data", () => Results.Text(DemoData.Csv, "text/csv"));

app.MapGet("/api/doctor", (DemoRunner runner) => Results.Json(new
{
    workerImage = runner.Options.WorkerImage,
    wallClockSeconds = runner.Options.WallClockTimeout.TotalSeconds,
    maxCompileAttempts = runner.Options.MaxCompileAttempts,
    dotnet = Environment.Version.ToString()
}));

app.MapGet("/api/runs/{scenario}/events", async (string scenario, HttpContext http, DemoRunner runner, CancellationToken cancellationToken) =>
{
    if (!DemoSession.IsKnown(scenario))
    {
        http.Response.StatusCode = StatusCodes.Status404NotFound;
        await http.Response.WriteAsync("Unknown scenario.", cancellationToken);
        return;
    }

    http.Response.Headers.CacheControl = "no-store";
    http.Response.ContentType = "text/event-stream";

    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, http.RequestAborted);
    var token = linked.Token;

    var run = runner.Start(scenario, token);
    try
    {
        await foreach (var runEvent in run.Run.Events(token))
        {
            await SendAsync(http.Response, ToPayload(runEvent), token);
        }

        await run.Run.Result;
        await SendAsync(http.Response, BuildResult(run.Session, runner.Options), token);
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
        // The client closed the stream; the run was cancelled with it.
    }
    catch (Exception exception)
    {
        try
        {
            await SendAsync(
                http.Response,
                new
                {
                    type = "run_error",
                    message = exception.Message,
                    compileAttempts = run.Session.CompileAttempts.Select(attempt => new
                    {
                        attempt = attempt.Attempt,
                        success = attempt.Success,
                        errors = attempt.Errors
                    })
                },
                CancellationToken.None);
        }
        catch (Exception sendException) when (sendException is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The browser went away before the error could be delivered.
        }
    }
});

await app.RunAsync();

static async Task SendAsync(HttpResponse response, object payload, CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(payload, EventJson.Options);
    await response.WriteAsync($"data: {json}\n\n", cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}

static object ToPayload(RunEvent runEvent) => runEvent switch
{
    RunStarted started => new { type = "run_started", flow = started.FlowName, timestamp = started.Timestamp },
    StepStarted started => new { type = "step_started", step = started.StepName, timestamp = started.Timestamp },
    StepCompleted completed => new
    {
        type = "step_completed",
        step = completed.StepName,
        ms = Math.Round(completed.Duration.TotalMilliseconds, 3),
        timestamp = completed.Timestamp
    },
    StepFailed failed => new
    {
        type = "step_failed",
        step = failed.StepName,
        code = failed.ErrorCode,
        message = failed.Message,
        timestamp = failed.Timestamp
    },
    StepRetryScheduled retry => new
    {
        type = "retry_scheduled",
        attempt = retry.Attempt,
        code = retry.ErrorCode,
        message = retry.Message,
        timestamp = retry.Timestamp
    },
    ModelCallStarted started => new
    {
        type = "model_call_started",
        step = started.StepName,
        model = started.ModelId,
        timestamp = started.Timestamp
    },
    ModelCallCompleted completed => new
    {
        type = "model_call_completed",
        step = completed.StepName,
        model = completed.ModelId,
        inputTokens = completed.InputTokens,
        outputTokens = completed.OutputTokens,
        ms = Math.Round(completed.Duration.TotalMilliseconds, 3),
        timestamp = completed.Timestamp
    },
    RunCompleted completed => new
    {
        type = "run_completed",
        ms = Math.Round(completed.Duration.TotalMilliseconds, 3),
        timestamp = completed.Timestamp
    },
    RunFailed failed => new
    {
        type = "run_failed",
        code = failed.ErrorCode,
        message = failed.Message,
        timestamp = failed.Timestamp
    },
    _ => new { type = runEvent.GetType().Name, timestamp = runEvent.Timestamp }
};

static object BuildResult(DemoSession session, DemoOptions options) => new
{
    type = "result",
    scenario = session.Scenario,
    request = session.Request,
    plan = session.Plan,
    script = session.Script,
    compileAttempts = session.CompileAttempts.Select(attempt => new
    {
        attempt = attempt.Attempt,
        success = attempt.Success,
        errors = attempt.Errors
    }),
    summary = session.Outcome?.Summary,
    charts = session.Outcome?.Charts.Select(chart => new
    {
        kind = chart.Kind,
        title = chart.Title,
        labels = chart.Labels,
        values = chart.Values
    }),
    logs = session.Outcome?.Logs,
    sandboxMs = session.Outcome is null ? (double?)null : Math.Round(session.Outcome.DurationMs, 3),
    workerImage = options.WorkerImage,
    wallClockSeconds = options.WallClockTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)
};

internal static class EventJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
