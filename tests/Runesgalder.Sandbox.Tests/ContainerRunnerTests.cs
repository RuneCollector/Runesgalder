using Runesgalder.Sandbox;
using Runesgalder.Sandbox.Containers;

namespace Runesgalder.SandboxTests;

public sealed class ContainerRunnerTests
{
    [Fact]
    public void BuildsLockedDownArgumentsByDefault()
    {
        var arguments = SandboxContainerRunner.BuildArguments(
            new ContainerRunRequest("worker:test", "jobs", "out"),
            "runesgalder-test");

        Assert.Equal("run", arguments[0]);
        Assert.Contains("--rm", arguments);
        Assert.Equal("none", ValueAfter(arguments, "--network"));
        Assert.Equal("268435456", ValueAfter(arguments, "--memory"));
        Assert.Equal("268435456", ValueAfter(arguments, "--memory-swap"));
        Assert.Equal("1", ValueAfter(arguments, "--cpus"));
        Assert.Contains("--read-only", arguments);
        Assert.Equal("ALL", ValueAfter(arguments, "--cap-drop"));
        Assert.Equal("no-new-privileges", ValueAfter(arguments, "--security-opt"));
        Assert.Equal("DOTNET_EnableDiagnostics=0", ValueAfter(arguments, "--env"));
        Assert.Contains(arguments, argument => argument.StartsWith("DOTNET_GCHeapHardLimit=0x", StringComparison.Ordinal));
        Assert.Contains(arguments, argument => argument.EndsWith("jobs:/work:ro", StringComparison.Ordinal));
        Assert.Contains(arguments, argument => argument.EndsWith("out:/out:rw", StringComparison.Ordinal));
        Assert.Equal("30000", ValueAfter(arguments, "--timeout-ms"));
        Assert.Equal("1048576", ValueAfter(arguments, "--max-output-bytes"));
    }

    [Fact]
    public void OmitsTheNetworkFlagWhenThePolicyAllowsNetwork()
    {
        var builder = SandboxPolicy.CreateBuilder();
        builder.NetworkEnabled = true;

        var arguments = SandboxContainerRunner.BuildArguments(
            new ContainerRunRequest("worker:test", "jobs", "out") { Policy = builder.Build() },
            "runesgalder-test");

        Assert.DoesNotContain("--network", arguments);
    }

    [Fact]
    public void UsesTheRequestedFileNames()
    {
        var arguments = SandboxContainerRunner.BuildArguments(
            new ContainerRunRequest("worker:test", "jobs", "out")
            {
                ScriptFileName = "analysis.dll",
                DataFileName = "input.csv",
                ResultFileName = "analysis.json"
            },
            "runesgalder-test");

        Assert.Contains("/work/analysis.dll", arguments);
        Assert.Contains("/work/input.csv", arguments);
        Assert.Contains("/out/analysis.json", arguments);
    }

    private static string? ValueAfter(IReadOnlyList<string> arguments, string flag)
    {
        for (var i = 0; i < arguments.Count - 1; i++)
        {
            if (string.Equals(arguments[i], flag, StringComparison.Ordinal))
            {
                return arguments[i + 1];
            }
        }

        return null;
    }
}
