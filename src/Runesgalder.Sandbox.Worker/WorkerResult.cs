using System.Text.Json.Serialization;

namespace Runesgalder.Sandbox.Worker;

/// <summary>The outcome of one worker run.</summary>
/// <param name="Success">Whether the analysis completed.</param>
/// <param name="Error">The error kind, or null on success.</param>
/// <param name="Message">The error message, or null on success.</param>
/// <param name="Summary">The analysis summary, or null on failure.</param>
/// <param name="Charts">The charts requested by the analysis.</param>
/// <param name="Logs">The lines written by the analysis.</param>
/// <param name="DurationMs">The wall-clock duration of the analysis.</param>
public sealed record WorkerResult(
    bool Success,
    string? Error,
    string? Message,
    string? Summary,
    IReadOnlyList<ChartResult> Charts,
    IReadOnlyList<string> Logs,
    double DurationMs)
{
    /// <summary>The error kinds reported by the worker.</summary>
    public static class ErrorKinds
    {
        /// <summary>The input or arguments were rejected.</summary>
        public const string Input = "input";

        /// <summary>The script failed the contract check.</summary>
        public const string Contract = "contract";

        /// <summary>The analysis exceeded the wall-clock limit.</summary>
        public const string Timeout = "timeout";

        /// <summary>The analysis exceeded the output quota.</summary>
        public const string OutputQuota = "output-quota";

        /// <summary>The analysis threw an exception.</summary>
        public const string Script = "script";
    }
}

/// <summary>A chart in the worker result.</summary>
/// <param name="Kind">The chart kind.</param>
/// <param name="Title">The chart title.</param>
/// <param name="Labels">The category labels.</param>
/// <param name="Values">The numeric values.</param>
public sealed record ChartResult(
    [property: JsonConverter(typeof(JsonStringEnumConverter<ChartKind>))] ChartKind Kind,
    string Title,
    IReadOnlyList<string> Labels,
    IReadOnlyList<double> Values);
