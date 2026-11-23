namespace Runesgalder.Mcp;

internal static class McpLimits
{
    public const int MaxSourceBytes = 256 * 1024;
    public const int MaxDataBytes = 8 * 1024 * 1024;
    public const int MaxDataRows = 100_000;
    public const int DefaultWaitSeconds = 30;
    public const int MaxWaitSeconds = 120;
    public const int DefaultWallClockSeconds = 30;
    public const int DefaultMaxWallClockSeconds = 120;
    public const int QueueCapacity = 5;
    public const int MaxRuns = 100;
    public const long MaxMemoryBytes = 256L * 1024 * 1024;
    public const long MaxOutputBytes = 1024L * 1024;
    public const int MaxCpuCores = 1;
    public const int MaxProcesses = 16;

    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);
}
