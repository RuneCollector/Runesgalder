namespace Runesgalder.Sandbox;

/// <summary>The charts the host can render.</summary>
public enum ChartKind
{
    /// <summary>A bar chart.</summary>
    Bar,

    /// <summary>A line chart.</summary>
    Line,

    /// <summary>A pie chart.</summary>
    Pie
}

/// <summary>A chart requested by generated analysis code.</summary>
/// <param name="Kind">The chart kind.</param>
/// <param name="Title">The chart title.</param>
/// <param name="Labels">The category labels.</param>
/// <param name="Values">The numeric values.</param>
public sealed record ChartSpec(
    ChartKind Kind,
    string Title,
    IReadOnlyList<string> Labels,
    IReadOnlyList<double> Values);
