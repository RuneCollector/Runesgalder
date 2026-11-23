using System.Globalization;
using System.Runtime.InteropServices;
using Runesgalder.Sandbox;

namespace Runesgalder.Cli;

internal static class CliApp
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        return args[0] switch
        {
            "doctor" => RunDoctor(),
            "demo" => await RunDemoAsync().ConfigureAwait(false),
            "resume" => await RunResumeAsync().ConfigureAwait(false),
            _ => UnknownCommand(args[0])
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Runesgalder CLI (pre-alpha)");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  runesgalder doctor   Print runtime and platform details.");
        Console.WriteLine("  runesgalder demo     Run a typed flow and print its journal.");
        Console.WriteLine("  runesgalder resume   Run a checkpointed flow twice and show the restored prefix.");
    }

    private static int RunDoctor()
    {
        var container = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");

        Console.WriteLine("Runesgalder doctor");
        Console.WriteLine($"Runtime:      {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"RID:          {RuntimeInformation.RuntimeIdentifier}");
        Console.WriteLine($"OS:           {RuntimeInformation.OSDescription}");
        Console.WriteLine($"Architecture: {RuntimeInformation.OSArchitecture}");
        Console.WriteLine($"Process:      {Environment.ProcessPath}");
        Console.WriteLine($"Container:    {(string.Equals(container, "true", StringComparison.OrdinalIgnoreCase) ? "yes" : "no")}");
        Console.WriteLine($"Sandbox host: {(RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux (supported)" : "not Linux; use WSL2 or Docker")}");
        return 0;
    }

    private static async Task<int> RunDemoAsync()
    {
        var flow = Flow.Start(Step.Define<string, int>("parse", input => int.Parse(input, CultureInfo.InvariantCulture)))
            .Then(Step.Define<int, int>("double", value => value * 2))
            .Then(Step.Define<int, string>("format", value => $"Result: {value.ToString(CultureInfo.InvariantCulture)}"));

        var journal = new InMemoryJournal();
        Console.WriteLine($"Flow: {flow.Name}");

        var result = await flow.RunAsync("21", journal).ConfigureAwait(false);
        Console.WriteLine(result);
        Console.WriteLine();
        Console.WriteLine("Journal:");
        PrintJournal(journal);

        var policy = SandboxPolicy.Default;
        Console.WriteLine();
        Console.WriteLine($"Sandbox network: {(policy.NetworkEnabled ? "enabled" : "denied by default")}");
        Console.WriteLine($"Sandbox memory:  {policy.MaxMemoryBytes / (1024 * 1024)} MiB");
        Console.WriteLine($"Sandbox timeout: {policy.WallClockTimeout.TotalSeconds:0} s");
        return 0;
    }

    private static async Task<int> RunResumeAsync()
    {
        var store = new InMemoryCheckpointStore();
        var prepareCalls = 0;
        var flow = Flow.Start(Step.Define<string, int>(
                    "prepare",
                    input =>
                    {
                        prepareCalls++;
                        return int.Parse(input, CultureInfo.InvariantCulture);
                    }))
            .Checkpoint(store, "cli-demo")
            .Then(Step.Define<int, string>(
                "format",
                value => $"Result: {(value * 2).ToString(CultureInfo.InvariantCulture)}"));

        Console.WriteLine($"Flow: {flow.Name}");

        var first = new InMemoryJournal();
        Console.WriteLine($"First run:  {await flow.RunAsync("21", first).ConfigureAwait(false)}");

        var second = new InMemoryJournal();
        Console.WriteLine($"Second run: {await flow.RunAsync("21", second).ConfigureAwait(false)}");
        Console.WriteLine($"prepare calls: {prepareCalls.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine();
        Console.WriteLine("First run journal:");
        PrintJournal(first);
        Console.WriteLine("Second run journal:");
        PrintJournal(second);
        return 0;
    }

    private static void PrintJournal(InMemoryJournal journal)
    {
        foreach (var runEvent in journal.Snapshot())
        {
            Console.WriteLine($"  {runEvent.Timestamp:O} {Describe(runEvent)}");
        }
    }

    private static string Describe(RunEvent runEvent) => runEvent switch
    {
        RunStarted started => $"RunStarted flow={started.FlowName}",
        StepStarted started => $"StepStarted {started.StepName}",
        StepCompleted completed => $"StepCompleted {completed.StepName} duration={completed.Duration.TotalMilliseconds:0.000}ms",
        StepFailed failed => $"StepFailed {failed.StepName} code={failed.ErrorCode} message={failed.Message}",
        StepRetryScheduled retry => $"StepRetryScheduled {retry.StepId} attempt={retry.Attempt}",
        ModelCallStarted started => $"ModelCallStarted {started.StepName} model={started.ModelId ?? "unknown"}",
        ModelCallCompleted completed =>
            $"ModelCallCompleted {completed.StepName} tokens={completed.InputTokens?.ToString(CultureInfo.InvariantCulture) ?? "-"}/" +
            $"{completed.OutputTokens?.ToString(CultureInfo.InvariantCulture) ?? "-"} " +
            $"duration={completed.Duration.TotalMilliseconds:0.000}ms",
        CheckpointStored stored => $"CheckpointStored key={stored.Key}",
        CheckpointRestored restored => $"CheckpointRestored key={restored.Key}",
        RunCompleted completed => $"RunCompleted duration={completed.Duration.TotalMilliseconds:0.000}ms",
        RunFailed failed => $"RunFailed code={failed.ErrorCode} message={failed.Message}",
        _ => runEvent.GetType().Name
    };

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 2;
    }
}
