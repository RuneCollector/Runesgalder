using System.Text.Json;
using Runesgalder.Sandbox;
using Runesgalder.Sandbox.Compilation;
using Runesgalder.Sandbox.Containers;

namespace Runesgalder.SandboxTests;

/// <summary>Runs the case files in security/escape-corpus against the worker image.</summary>
public sealed class EscapeCorpusTests
{
    private const string WorkerImage = "runesgalder-worker:dev";

    private static readonly JsonSerializerOptions CaseOptions =
        new() { PropertyNameCaseInsensitive = true };

    [DockerFact]
    public async Task EveryCaseProducesItsExpectedOutcome()
    {
        var corpusRoot = FindCorpusRoot();
        var cases = LoadCases(corpusRoot);
        Assert.NotEmpty(cases);

        var failures = new List<string>();
        foreach (var testCase in cases)
        {
            var (outcome, summary, detail) = await ExecuteAsync(corpusRoot, testCase);
            if (outcome != testCase.Expected)
            {
                failures.Add($"{testCase.Id}: expected '{testCase.Expected}' but got '{outcome}' ({detail})");
                continue;
            }

            if (testCase.ExpectedSummary is not null && summary != testCase.ExpectedSummary)
            {
                failures.Add(
                    $"{testCase.Id}: expected summary '{testCase.ExpectedSummary}' but got '{summary}'");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static async Task<(string Outcome, string? Summary, string Detail)> ExecuteAsync(
        string corpusRoot,
        CorpusCase testCase)
    {
        var source = File.ReadAllText(Path.Combine(corpusRoot, testCase.Source));
        var compilation = new ScriptCompiler().Compile(source);

        if (!compilation.Success)
        {
            var outcome = compilation.Diagnostics.Any(d => d.Id == "RGS0001")
                ? "policy_denied"
                : "compile_error";
            var ids = string.Join(",", compilation.Diagnostics.Select(d => d.Id).Distinct());
            return (outcome, null, "compile diagnostics: " + ids);
        }

        var builder = SandboxPolicy.CreateBuilder();
        if (testCase.WallClockSeconds is { } seconds)
        {
            builder.WallClockTimeout = TimeSpan.FromSeconds(seconds);
        }

        using var workspace = new CorpusWorkspace();
        workspace.Write("script.dll", compilation.Assembly!);
        workspace.Write("data.csv", "a\r\n1\r\n");

        var run = await SandboxContainerRunner.RunAsync(
            new ContainerRunRequest(WorkerImage, workspace.JobDirectory, workspace.OutputDirectory)
            {
                Policy = builder.Build()
            });

        if (run.TimedOut)
        {
            return ("timed_out", null, "the run was stopped at the wall-clock deadline");
        }

        if (!run.ResultWritten)
        {
            var outcome = run.ExitCode == 0 ? "runtime_error" : "resource_exceeded";
            return (outcome, null, $"no result file; exit code {run.ExitCode}");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(run.ResultPath));
        var root = document.RootElement;
        if (root.GetProperty("success").GetBoolean())
        {
            var summary = root.TryGetProperty("summary", out var value) ? value.GetString() : null;
            return ("ok", summary, "the script returned a result");
        }

        var error = root.TryGetProperty("error", out var code) ? code.GetString() : null;
        var message = root.TryGetProperty("message", out var text) ? text.GetString() ?? string.Empty : string.Empty;
        var mapped = error switch
        {
            "output-quota" => "resource_exceeded",
            "timeout" => "timed_out",
            "script" when message.Contains("OutOfMemoryException", StringComparison.Ordinal) =>
                "resource_exceeded",
            _ => "runtime_error"
        };
        return (mapped, null, $"error '{error}': {message}");
    }

    private static string FindCorpusRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "security", "escape-corpus", "cases.json");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "security/escape-corpus/cases.json was not found above " + AppContext.BaseDirectory);
    }

    private static List<CorpusCase> LoadCases(string corpusRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(corpusRoot, "cases.json")));
        var cases = document.RootElement.GetProperty("cases")
            .Deserialize<List<CorpusCase>>(CaseOptions);
        return cases ?? [];
    }

    private sealed record CorpusCase(
        string Id,
        string Category,
        string Description,
        string Tier,
        string Policy,
        string Source,
        string Expected,
        string Notes,
        int? WallClockSeconds = null,
        string? ExpectedSummary = null);

    private sealed class CorpusWorkspace : IDisposable
    {
        public CorpusWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "runesgalder-corpus-" + Guid.NewGuid().ToString("N"));
            JobDirectory = Path.Combine(Root, "job");
            OutputDirectory = Path.Combine(Root, "out");
            Directory.CreateDirectory(JobDirectory);
            Directory.CreateDirectory(OutputDirectory);
        }

        private string Root { get; }

        public string JobDirectory { get; }

        public string OutputDirectory { get; }

        public void Write(string name, string content) =>
            Write(name, System.Text.Encoding.UTF8.GetBytes(content));

        public void Write(string name, byte[] content) =>
            File.WriteAllBytes(Path.Combine(JobDirectory, name), content);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The container or the OS may still hold a file; the temp directory is disposable.
            }
        }
    }
}
