namespace Runesgalder.Sandbox;

/// <summary>Narrow capabilities exposed to generated analysis code.</summary>
public interface IScriptContext
{
    /// <summary>The execution cancellation token.</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>The current time, deterministic when enabled by policy.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Returns the next deterministic random value in [0, 1).</summary>
    /// <returns>A pseudo-random value.</returns>
    double NextDouble();

    /// <summary>Writes a log line, subject to the output quota.</summary>
    /// <param name="message">The message.</param>
    void Log(string message);

    /// <summary>Requests a chart from the host renderer.</summary>
    /// <param name="chart">The chart specification.</param>
    void Chart(ChartSpec chart);
}
