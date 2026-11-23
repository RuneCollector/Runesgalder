namespace Runesgalder.Sandbox.Containers;

/// <summary>A containerised analysis job.</summary>
/// <param name="Image">The worker image to run.</param>
/// <param name="JobDirectory">The directory holding the script assembly and the CSV file.</param>
/// <param name="OutputDirectory">The directory that receives the result JSON.</param>
public sealed record ContainerRunRequest(string Image, string JobDirectory, string OutputDirectory)
{
    /// <summary>The policy that shapes the container flags.</summary>
    public SandboxPolicy Policy { get; init; } = SandboxPolicy.Default;

    /// <summary>The script assembly file name inside the job directory.</summary>
    public string ScriptFileName { get; init; } = "script.dll";

    /// <summary>The CSV file name inside the job directory.</summary>
    public string DataFileName { get; init; } = "data.csv";

    /// <summary>The result file name inside the output directory.</summary>
    public string ResultFileName { get; init; } = "result.json";

    /// <summary>The process and thread limit passed to the container.</summary>
    public int PidsLimit { get; init; } = 64;

    /// <summary>Extra time allowed beyond the policy timeout before the container is killed.</summary>
    public TimeSpan HardKillGrace { get; init; } = TimeSpan.FromSeconds(5);
}
