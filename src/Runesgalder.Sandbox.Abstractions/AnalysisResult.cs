namespace Runesgalder.Sandbox;

/// <summary>The result returned by a generated analysis.</summary>
/// <param name="Summary">A short textual summary.</param>
/// <param name="Charts">The charts requested by the analysis.</param>
public sealed record AnalysisResult(string Summary, IReadOnlyList<ChartSpec> Charts);
