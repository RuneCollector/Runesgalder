namespace Runesgalder;

/// <summary>Hard limits and optional warn thresholds for a governed segment.</summary>
/// <remarks>
/// At least one hard limit is required and every set value must be positive. Limits are
/// validated when the budget is attached, not by the record, so stored payloads stay readable.
/// </remarks>
public sealed record Budget
{
    /// <summary>The hard token limit.</summary>
    public long? MaxTokens { get; init; }

    /// <summary>The hard cost limit in US dollars.</summary>
    public decimal? MaxUsd { get; init; }

    /// <summary>The hard wall-clock limit.</summary>
    public TimeSpan? MaxWall { get; init; }

    /// <summary>The token count that raises a warning.</summary>
    public long? WarnTokens { get; init; }

    /// <summary>The cost in US dollars that raises a warning.</summary>
    public decimal? WarnUsd { get; init; }

    /// <summary>The wall-clock time that raises a warning.</summary>
    public TimeSpan? WarnWall { get; init; }
}
