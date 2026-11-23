using System.Diagnostics;

namespace Runesgalder.McpTests;

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
