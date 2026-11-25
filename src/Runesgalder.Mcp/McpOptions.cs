using System.Globalization;
using System.Reflection;

namespace Runesgalder.Mcp;

internal sealed record McpOptions
{
    public const string WorkerImageVariable = "RUNESGALDER_MCP_WORKER_IMAGE";
    public const string MaxWallClockVariable = "RUNESGALDER_MCP_MAX_WALL_CLOCK_SECONDS";
    public const string DefaultWorkerImage = "runesgalder-worker:dev";
    public const string ServerName = "runesgalder";
    public const string AnalysisContract = "analysis";

    public static string ServerVersion { get; } =
        typeof(McpOptions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+')[0] ?? "0.0.0";

    public string WorkerImage { get; init; } = DefaultWorkerImage;

    public TimeSpan MaxWallClock { get; init; } = TimeSpan.FromSeconds(McpLimits.DefaultMaxWallClockSeconds);

    public TimeSpan DefaultWallClock =>
        TimeSpan.FromSeconds(Math.Min(McpLimits.DefaultWallClockSeconds, MaxWallClock.TotalSeconds));

    public static McpOptions FromEnvironment()
    {
        var image = Environment.GetEnvironmentVariable(WorkerImageVariable);
        if (string.IsNullOrWhiteSpace(image))
        {
            image = DefaultWorkerImage;
        }

        var maxWallClock = TimeSpan.FromSeconds(McpLimits.DefaultMaxWallClockSeconds);
        var rawMax = Environment.GetEnvironmentVariable(MaxWallClockVariable);
        if (double.TryParse(rawMax, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
            seconds > 0 &&
            double.IsFinite(seconds))
        {
            maxWallClock = TimeSpan.FromSeconds(Math.Min(seconds, McpLimits.DefaultMaxWallClockSeconds));
        }

        return new McpOptions
        {
            WorkerImage = image.Trim(),
            MaxWallClock = maxWallClock
        };
    }
}
