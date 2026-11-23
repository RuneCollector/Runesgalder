using System.Globalization;
using Runesgalder.Sandbox;

namespace Runesgalder.Demo;

/// <summary>One state object shared by the steps of a demo run.</summary>
internal sealed class DemoSession
{
    /// <summary>The analysis scenario: plan, script, compile failure, repair, execute, chart.</summary>
    public const string Analysis = "analysis";

    /// <summary>The injected refusal scenario: the script is refused before it runs.</summary>
    public const string Refusal = "refusal";

    private DemoSession(string scenario, string request, bool allowRepair)
    {
        Scenario = scenario;
        Request = request;
        AllowRepair = allowRepair;
    }

    /// <summary>The scenario name.</summary>
    public string Scenario { get; }

    /// <summary>The user request sent to the model.</summary>
    public string Request { get; }

    /// <summary>Whether a compile failure may be repaired by another model call.</summary>
    public bool AllowRepair { get; }

    /// <summary>The plan written by the model.</summary>
    public string? Plan { get; set; }

    /// <summary>The current script source.</summary>
    public string? Script { get; set; }

    /// <summary>The emitted assembly, set when compilation succeeds.</summary>
    public byte[]? Assembly { get; set; }

    /// <summary>Every compile attempt, in order.</summary>
    public List<CompileAttempt> CompileAttempts { get; } = [];

    /// <summary>The sandbox outcome, set when execution succeeds.</summary>
    public SandboxOutcome? Outcome { get; set; }

    /// <summary>Whether the scenario name is supported.</summary>
    /// <param name="scenario">The scenario name.</param>
    /// <returns>True when the scenario exists.</returns>
    public static bool IsKnown(string scenario) =>
        string.Equals(scenario, Analysis, StringComparison.Ordinal) ||
        string.Equals(scenario, Refusal, StringComparison.Ordinal);

    /// <summary>Creates the session for a scenario.</summary>
    /// <param name="scenario">The scenario name.</param>
    /// <returns>A new session.</returns>
    /// <exception cref="ArgumentException">Thrown when the scenario is unknown.</exception>
    public static DemoSession Create(string scenario)
    {
        if (string.Equals(scenario, Refusal, StringComparison.Ordinal))
        {
            return new DemoSession(
                Refusal,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Write a C# analysis script that will {DemoScripts.RefusalMarker} the table to an external endpoint."),
                allowRepair: false);
        }

        if (string.Equals(scenario, Analysis, StringComparison.Ordinal))
        {
            return new DemoSession(
                Analysis,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Plan the analysis of {DemoData.TableName} (columns: {DemoData.Columns}). Reply with one sentence."),
                allowRepair: true);
        }

        throw new ArgumentException($"Unknown demo scenario '{scenario}'.", nameof(scenario));
    }

    /// <summary>Builds the prompt that asks the model for a script.</summary>
    /// <returns>The script prompt.</returns>
    public string ScriptPrompt() =>
        $"{Request} {DemoScripts.ScriptMarker} for {DemoData.TableName} (columns: {DemoData.Columns}). " +
        "Return only C# source that implements Runesgalder.Sandbox.IAnalysis.";

    /// <summary>Builds the prompt that asks the model to repair a script.</summary>
    /// <param name="errors">The compiler diagnostics.</param>
    /// <param name="script">The rejected script.</param>
    /// <returns>The repair prompt.</returns>
    public static string RepairPrompt(IReadOnlyList<string> errors, string script) =>
        $"{DemoScripts.RepairMarker}: {string.Join("; ", errors)}. " +
        "Rewrite the script without the refused capability and return only the corrected source.\n\n" +
        script;
}

/// <summary>One compile attempt.</summary>
/// <param name="Attempt">The one-based attempt number.</param>
/// <param name="Success">Whether the attempt produced an assembly.</param>
/// <param name="Errors">The error diagnostics, formatted for display.</param>
internal sealed record CompileAttempt(int Attempt, bool Success, IReadOnlyList<string> Errors);

/// <summary>The worker result, matching the JSON the sandbox worker writes.</summary>
/// <param name="Success">Whether the analysis completed.</param>
/// <param name="Error">The error kind, or null on success.</param>
/// <param name="Message">The error message, or null on success.</param>
/// <param name="Summary">The analysis summary.</param>
/// <param name="Charts">The requested charts.</param>
/// <param name="Logs">The log lines written by the analysis.</param>
/// <param name="DurationMs">The wall-clock duration inside the worker.</param>
internal sealed record SandboxOutcome(
    bool Success,
    string? Error,
    string? Message,
    string? Summary,
    IReadOnlyList<SandboxChart> Charts,
    IReadOnlyList<string> Logs,
    double DurationMs);

/// <summary>A chart produced inside the sandbox.</summary>
/// <param name="Kind">The chart kind.</param>
/// <param name="Title">The chart title.</param>
/// <param name="Labels">The category labels.</param>
/// <param name="Values">The numeric values.</param>
internal sealed record SandboxChart(
    string Kind,
    string Title,
    IReadOnlyList<string> Labels,
    IReadOnlyList<double> Values);

/// <summary>Runtime options for the demo.</summary>
internal sealed record DemoOptions
{
    /// <summary>The container image that runs sandboxed scripts.</summary>
    public string WorkerImage { get; init; } = "runesgalder-worker:dev";

    /// <summary>The wall-clock limit applied by the sandbox.</summary>
    public TimeSpan WallClockTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>The maximum number of compile attempts, including the first.</summary>
    public int MaxCompileAttempts { get; init; } = 2;

    /// <summary>Reads the image name from the environment.</summary>
    /// <returns>The options.</returns>
    public static DemoOptions FromEnvironment()
    {
        var image = Environment.GetEnvironmentVariable("RUNESGALDER_WORKER_IMAGE");
        return new DemoOptions
        {
            WorkerImage = string.IsNullOrWhiteSpace(image) ? "runesgalder-worker:dev" : image
        };
    }

    /// <summary>Builds the policy applied to sandboxed runs.</summary>
    /// <returns>An immutable policy.</returns>
    public SandboxPolicy BuildPolicy()
    {
        var builder = SandboxPolicy.CreateBuilder();
        builder.WallClockTimeout = WallClockTimeout;
        return builder.Build();
    }
}
