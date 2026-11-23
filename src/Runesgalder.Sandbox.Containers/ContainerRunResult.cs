namespace Runesgalder.Sandbox.Containers;

/// <summary>The outcome of a containerised analysis job.</summary>
/// <param name="TimedOut">Whether the hard deadline expired and the container was killed.</param>
/// <param name="ExitCode">The container exit code, or -1 when it was killed.</param>
/// <param name="StandardError">A bounded capture of the container's standard error.</param>
/// <param name="ResultPath">The expected path of the result JSON.</param>
/// <param name="ResultWritten">Whether the result JSON exists.</param>
public sealed record ContainerRunResult(
    bool TimedOut,
    int ExitCode,
    string StandardError,
    string ResultPath,
    bool ResultWritten)
{
    /// <summary>Whether the job finished cleanly and wrote its result.</summary>
    public bool Succeeded => !TimedOut && ExitCode == 0 && ResultWritten;
}
