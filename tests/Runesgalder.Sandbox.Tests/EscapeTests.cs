using System.Diagnostics;
using System.Text.Json;
using Runesgalder.Sandbox;
using Runesgalder.Sandbox.Compilation;
using Runesgalder.Sandbox.Containers;

namespace Runesgalder.SandboxTests;

/// <summary>Container-level containment checks for the worker image.</summary>
public sealed class EscapeTests
{
    private const string WorkerImage = "runesgalder-worker:dev";

    [DockerFact]
    public async Task HostEnvironmentIsNotVisibleInsideTheWorker()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
                {
                    var secret = Environment.GetEnvironmentVariable("RUNESGALDER_ESCAPE_SECRET") ?? "";
                    return Task.FromResult(new AnalysisResult(secret.Length + "|" + Environment.CurrentDirectory, []));
                }
            }
            """;

        Environment.SetEnvironmentVariable("RUNESGALDER_ESCAPE_SECRET", "host-only", EnvironmentVariableTarget.Process);
        try
        {
            using var workspace = new EscapeWorkspace();
            var run = await RunAsync(workspace, source,
SandboxPolicy.CreateBuilder().Build());
            var result = ReadResult(run);
            Assert.True(result.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("0|/app", result.RootElement.GetProperty("summary").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("RUNESGALDER_ESCAPE_SECRET", null, EnvironmentVariableTarget.Process);
        }
    }

    [DockerFact]
    public async Task ANonCooperativeLoopIsKilledAtTheHardDeadline()
    {
        const string source = """
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
                {
                    while (true)
                    {
                    }
                }
            }
            """;

        var builder = SandboxPolicy.CreateBuilder();
        builder.WallClockTimeout = TimeSpan.FromSeconds(2);
        var stopwatch = Stopwatch.StartNew();

        using var workspace = new EscapeWorkspace();

        var run = await RunAsync(workspace, source,
builder.Build());

        stopwatch.Stop();
        Assert.True(run.TimedOut);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"The kill took {stopwatch.Elapsed}.");
    }

    [DockerFact]
    public async Task AMemoryBombCannotExceedTheContainerLimit()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
                {
                    var block = new byte[512 * 1024 * 1024];
                    Array.Fill(block, (byte)1);
                    return Task.FromResult(new AnalysisResult("allocated " + block.Length, []));
                }
            }
            """;

        using var workspace = new EscapeWorkspace();

        var run = await RunAsync(workspace, source,
SandboxPolicy.CreateBuilder().Build());

        if (run.ResultWritten)
        {
            var result = ReadResult(run);
            Assert.False(result.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("script", result.RootElement.GetProperty("error").GetString());
            Assert.Contains(
                "OutOfMemoryException",
                result.RootElement.GetProperty("message").GetString() ?? string.Empty,
                StringComparison.Ordinal);
        }
        else
        {
            Assert.NotEqual(0, run.ExitCode);
        }
    }

    [DockerFact]
    public async Task AnOutputBombIsStoppedByTheWorkerQuota()
    {
        const string source = """
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
                {
                    context.Log(new string('x', 4 * 1024 * 1024));
                    return Task.FromResult(new AnalysisResult("unreachable", []));
                }
            }
            """;

        using var workspace = new EscapeWorkspace();

        var run = await RunAsync(workspace, source,
SandboxPolicy.CreateBuilder().Build());

        var result = ReadResult(run);
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("output-quota", result.RootElement.GetProperty("error").GetString());
    }

    [DockerFact]
    public async Task ExitingTheProcessCannotForgeASuccessfulResult()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
                {
                    Environment.Exit(0);
                    return Task.FromResult(new AnalysisResult("forged", []));
                }
            }
            """;

        using var workspace = new EscapeWorkspace();

        var run = await RunAsync(workspace, source,
SandboxPolicy.CreateBuilder().Build());

        Assert.False(run.ResultWritten);
    }

    private static async Task<ContainerRunResult> RunAsync(EscapeWorkspace workspace, string source, SandboxPolicy policy)
    {
        var compilation = new ScriptCompiler().Compile(source);
        Assert.True(
            compilation.Success,
            string.Join("; ", compilation.Diagnostics.Select(diagnostic => $"{diagnostic.Id} {diagnostic.Message}")));

        workspace.Write("script.dll", compilation.Assembly!);
        workspace.Write("data.csv", "a\r\n1\r\n");

        var request = new ContainerRunRequest(WorkerImage, workspace.JobDirectory, workspace.OutputDirectory)
        {
            Policy = policy
        };

        return await SandboxContainerRunner.RunAsync(request);
    }

    private static JsonDocument ReadResult(ContainerRunResult run)
    {
        Assert.True(run.ResultWritten, $"No result file at {run.ResultPath}. stderr: {run.StandardError}");
        return JsonDocument.Parse(File.ReadAllText(run.ResultPath));
    }

    private sealed class EscapeWorkspace : IDisposable
    {
        public EscapeWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "runesgalder-escape-" + Guid.NewGuid().ToString("N"));
            JobDirectory = Path.Combine(Root, "job");
            OutputDirectory = Path.Combine(Root, "out");
            Directory.CreateDirectory(JobDirectory);
            Directory.CreateDirectory(OutputDirectory);
        }

        private string Root { get; }

        public string JobDirectory { get; }

        public string OutputDirectory { get; }

        public void Write(string name, string content) => Write(name, System.Text.Encoding.UTF8.GetBytes(content));

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

/// <summary>A fact that runs only where Docker and the worker image are available.</summary>
internal sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        var (available, reason) = DockerProbe.State.Value;
        if (!available)
        {
            Skip = reason;
        }
    }
}

internal static class DockerProbe
{
    public static readonly Lazy<(bool Available, string Reason)> State = new(Probe);

    private static (bool Available, string Reason) Probe()
    {
        try
        {
            var os = Capture("version", "--format", "{{.Server.Os}}");
            if (!string.Equals(os.Trim(), "linux", StringComparison.Ordinal))
            {
                return (false, "Docker is not running Linux containers.");
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or TimeoutException)
        {
            return (false, "Docker is not available: " + exception.Message);
        }

        try
        {
            Capture("image", "inspect", "runesgalder-worker:dev");
            return (true, string.Empty);
        }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
        {
            return (false, "Build the worker image first: runesgalder-worker:dev.");
        }
    }

    private static string Capture(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("docker could not be started.");

        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("docker did not answer within 10 seconds.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("docker exited with " + process.ExitCode + ": " + process.StandardError.ReadToEnd());
        }

        return output;
    }
}
