using System.Text;

namespace Runesgalder.Sandbox.Worker;

internal sealed class AnalysisScriptContext : IScriptContext
{
    private readonly List<string> _logs = [];
    private readonly List<ChartSpec> _charts = [];
    private readonly long _maxOutputBytes;
    private readonly Random _random = new(0);
    private long _outputBytes;

    public AnalysisScriptContext(long maxOutputBytes, bool deterministic, CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
        _maxOutputBytes = maxOutputBytes;
        UtcNow = deterministic ? DateTimeOffset.UnixEpoch : DateTimeOffset.UtcNow;
    }

    public CancellationToken CancellationToken { get; }

    public DateTimeOffset UtcNow { get; }

    public IReadOnlyList<string> Logs => _logs;

    public IReadOnlyList<ChartSpec> Charts => _charts;

    public double NextDouble() => _random.NextDouble();

    public void Log(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Charge(Encoding.UTF8.GetByteCount(message));
        _logs.Add(message);
    }

    public void Chart(ChartSpec chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        Charge(EstimateChartBytes(chart));
        _charts.Add(chart);
    }

    private static long EstimateChartBytes(ChartSpec chart) =>
        Encoding.UTF8.GetByteCount(chart.Title) +
        16L +
        chart.Labels.Sum(Encoding.UTF8.GetByteCount) +
        (chart.Values.Count * sizeof(double));

    private void Charge(long bytes)
    {
        _outputBytes += bytes;
        if (_outputBytes > _maxOutputBytes)
        {
            throw new OutputQuotaExceededException(
                $"The analysis produced more than {_maxOutputBytes} bytes of output.");
        }
    }
}

internal sealed class OutputQuotaExceededException(string message) : Exception(message);
