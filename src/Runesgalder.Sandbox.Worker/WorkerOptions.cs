namespace Runesgalder.Sandbox.Worker;

/// <summary>Inputs for one worker run.</summary>
/// <param name="ScriptPath">The compiled analysis assembly.</param>
/// <param name="DataPath">The input CSV file.</param>
/// <param name="OutputPath">Where the result JSON is written.</param>
public sealed record WorkerOptions(string ScriptPath, string DataPath, string OutputPath)
{
    /// <summary>The wall-clock limit for the analysis.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The maximum log and chart output in bytes.</summary>
    public long MaxOutputBytes { get; init; } = 64L * 1024;

    /// <summary>The maximum number of data rows.</summary>
    public int MaxRows { get; init; } = 100_000;

    /// <summary>The maximum input CSV size in bytes.</summary>
    public long MaxDataBytes { get; init; } = 8L * 1024 * 1024;

    /// <summary>Whether the clock and random source are fixed for reproducible runs.</summary>
    public bool Deterministic { get; init; } = true;
}
