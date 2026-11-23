using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Runesgalder.Sandbox.Containers;

/// <summary>Runs a sandbox worker in a locked-down container and enforces the hard deadline.</summary>
public static class SandboxContainerRunner
{
    private const int StandardErrorLimit = 8192;

    /// <summary>Runs one job.</summary>
    /// <param name="request">The job.</param>
    /// <param name="cancellationToken">A token that stops the container.</param>
    /// <returns>The container outcome.</returns>
    /// <exception cref="OperationCanceledException">The caller cancelled the run.</exception>
    public static async Task<ContainerRunResult> RunAsync(
        ContainerRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = "runesgalder-" + Guid.NewGuid().ToString("N")[..12];
        var arguments = BuildArguments(request, name);
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

        var standardError = new StringBuilder();
        var drainOutput = DrainAsync(process.StandardOutput, null);
        var drainError = DrainAsync(process.StandardError, standardError);

        var deadline = request.Policy.WallClockTimeout + request.HardKillGrace;
        using var deadlineSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadlineSource.CancelAfter(deadline);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(deadlineSource.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            await KillAsync(name);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        await Task.WhenAll(drainOutput, drainError);

        cancellationToken.ThrowIfCancellationRequested();

        var resultPath = Path.Combine(request.OutputDirectory, request.ResultFileName);
        return new ContainerRunResult(
            timedOut,
            timedOut ? -1 : process.ExitCode,
            standardError.ToString(),
            resultPath,
            File.Exists(resultPath));
    }

    internal static IReadOnlyList<string> BuildArguments(ContainerRunRequest request, string name)
    {
        var policy = request.Policy;
        var arguments = new List<string>
        {
            "run",
            "--rm",
            "--name",
            name
        };

        if (!policy.NetworkEnabled)
        {
            arguments.Add("--network");
            arguments.Add("none");
        }

        arguments.Add("--memory");
        arguments.Add(policy.MaxMemoryBytes.ToString(CultureInfo.InvariantCulture));
        arguments.Add("--memory-swap");
        arguments.Add(policy.MaxMemoryBytes.ToString(CultureInfo.InvariantCulture));
        arguments.Add("--cpus");
        arguments.Add(policy.MaxCpuCores.ToString(CultureInfo.InvariantCulture));
        arguments.Add("--pids-limit");
        arguments.Add(request.PidsLimit.ToString(CultureInfo.InvariantCulture));
        arguments.Add("--read-only");
        arguments.Add("--tmpfs");
        arguments.Add("/tmp:rw,size=16m");
        arguments.Add("--cap-drop");
        arguments.Add("ALL");
        arguments.Add("--security-opt");
        arguments.Add("no-new-privileges");
        arguments.Add("--env");
        arguments.Add("DOTNET_EnableDiagnostics=0");
        arguments.Add("--env");
        arguments.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"DOTNET_GCHeapHardLimit=0x{policy.MaxMemoryBytes:X}"));
        arguments.Add("--volume");
        arguments.Add(Mount(request.JobDirectory, "/work", readOnly: true));
        arguments.Add("--volume");
        arguments.Add(Mount(request.OutputDirectory, "/out", readOnly: false));
        arguments.Add(request.Image);
        arguments.Add("--script");
        arguments.Add("/work/" + request.ScriptFileName);
        arguments.Add("--data");
        arguments.Add("/work/" + request.DataFileName);
        arguments.Add("--out");
        arguments.Add("/out/" + request.ResultFileName);
        arguments.Add("--timeout-ms");
        arguments.Add(((long)policy.WallClockTimeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
        arguments.Add("--max-output-bytes");
        arguments.Add(policy.MaxOutputBytes.ToString(CultureInfo.InvariantCulture));

        return arguments;
    }

    private static string Mount(string hostDirectory, string target, bool readOnly)
    {
        var host = Path.GetFullPath(hostDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Concat(host, ":", target, readOnly ? ":ro" : ":rw");
    }

    private static async Task KillAsync(string name)
    {
        await TryDockerAsync("kill", name);
        await TryDockerAsync("rm", "-f", name);
    }

    private static async Task TryDockerAsync(params string[] arguments)
    {
        try
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

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return;
            }

            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            // Cleanup is best-effort; the deadline path must not hide the original outcome.
        }
    }

    private static async Task DrainAsync(StreamReader reader, StringBuilder? sink)
    {
        var buffer = new char[1024];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            if (sink is not null && sink.Length < StandardErrorLimit)
            {
                sink.Append(buffer, 0, Math.Min(read, StandardErrorLimit - sink.Length));
            }
        }
    }
}
