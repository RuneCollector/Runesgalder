using System.Text;
using Runesgalder.Sandbox;
using Runesgalder.Sandbox.Containers;

namespace Runesgalder.Mcp;

internal interface ISandboxExecutor
{
    Task<SandboxOutcome> ExecuteAsync(
        byte[] assembly,
        string dataCsv,
        TimeSpan wallClock,
        CancellationToken cancellationToken);
}

internal sealed record SandboxOutcome(
    bool TimedOut,
    bool ResultWritten,
    string? ResultJson,
    long OutputBytes,
    int ExitCode = 0);

internal sealed class ContainerSandboxExecutor(string workerImage) : ISandboxExecutor
{
    public async Task<SandboxOutcome> ExecuteAsync(
        byte[] assembly,
        string dataCsv,
        TimeSpan wallClock,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), "runesgalder-mcp", Guid.NewGuid().ToString("N"));
        var jobDirectory = Path.Combine(root, "work");
        var outputDirectory = Path.Combine(root, "out");

        try
        {
            Directory.CreateDirectory(jobDirectory);
            Directory.CreateDirectory(outputDirectory);

            await File.WriteAllBytesAsync(
                Path.Combine(jobDirectory, "script.dll"),
                assembly,
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(jobDirectory, "data.csv"),
                dataCsv,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);

            var policy = SandboxPolicy.CreateBuilder();
            policy.MaxMemoryBytes = McpLimits.MaxMemoryBytes;
            policy.WallClockTimeout = wallClock;
            policy.MaxCpuCores = McpLimits.MaxCpuCores;
            policy.MaxProcesses = McpLimits.MaxProcesses;
            policy.MaxOutputBytes = McpLimits.MaxOutputBytes;

            var request = new ContainerRunRequest(workerImage, jobDirectory, outputDirectory)
            {
                Policy = policy.Build()
            };

            var result = await SandboxContainerRunner
                .RunAsync(request, cancellationToken)
                .ConfigureAwait(false);

            string? json = null;
            long outputBytes = 0;
            if (result.ResultWritten)
            {
                outputBytes = new FileInfo(result.ResultPath).Length;
                json = await File.ReadAllTextAsync(result.ResultPath, cancellationToken).ConfigureAwait(false);
            }

            return new SandboxOutcome(
                result.TimedOut,
                result.ResultWritten,
                json,
                outputBytes,
                result.ExitCode);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup; the run result is already captured.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup; the run result is already captured.
            }
        }
    }
}
