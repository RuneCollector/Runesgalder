using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Runesgalder.Sandbox.Containers;

namespace Runesgalder.Demo;

/// <summary>
/// Writes the compiled script and CSV to a job directory, runs the sandbox worker container,
/// and reads the result JSON it writes.
/// </summary>
internal sealed class ExecuteStep(DemoOptions options) : Step<DemoSession, DemoSession>("execute")
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public override async ValueTask<DemoSession> ExecuteAsync(
        DemoSession session,
        FlowContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var assembly = session.Assembly
            ?? throw new InvalidOperationException("No assembly was produced.");

        var root = Path.Combine(Path.GetTempPath(), "runesgalder-demo", Guid.NewGuid().ToString("N"));
        var jobDirectory = Path.Combine(root, "job");
        var outputDirectory = Path.Combine(root, "out");
        Directory.CreateDirectory(jobDirectory);
        Directory.CreateDirectory(outputDirectory);

        try
        {
            await File.WriteAllBytesAsync(Path.Combine(jobDirectory, "script.dll"), assembly, cancellationToken)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(
                    Path.Combine(jobDirectory, "data.csv"),
                    DemoData.Csv,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);

            var request = new ContainerRunRequest(options.WorkerImage, jobDirectory, outputDirectory)
            {
                Policy = options.BuildPolicy()
            };

            var run = await RunContainerAsync(request, cancellationToken).ConfigureAwait(false);
            if (run.TimedOut)
            {
                throw new InvalidOperationException("The sandbox exceeded its wall-clock limit and was killed.");
            }

            if (!run.Succeeded)
            {
                throw new InvalidOperationException(
                    $"The sandbox exited with code {run.ExitCode} and the result was {run.StandardError.Trim()}.");
            }

            var json = await File.ReadAllTextAsync(run.ResultPath, cancellationToken).ConfigureAwait(false);
            var outcome = JsonSerializer.Deserialize<SandboxOutcome>(json, JsonOptions)
                ?? throw new InvalidOperationException("The sandbox wrote an empty result.");

            if (!outcome.Success)
            {
                throw new InvalidOperationException(
                    $"The analysis failed inside the sandbox ({outcome.Error}): {outcome.Message}");
            }

            session.Outcome = outcome;
            return session;
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static async Task<ContainerRunResult> RunContainerAsync(
        ContainerRunRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SandboxContainerRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "Docker is required to execute sandboxed scripts. Build the worker image first: " +
                "dotnet publish src/Runesgalder.Sandbox.Worker/Runesgalder.Sandbox.Worker.csproj -c Release -o artifacts/worker && " +
                "docker build -f src/Runesgalder.Sandbox.Worker/Dockerfile -t runesgalder-worker:dev artifacts/worker",
                exception);
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Temporary files are best-effort cleanup only.
        }
    }
}
