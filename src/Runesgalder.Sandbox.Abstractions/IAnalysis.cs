namespace Runesgalder.Sandbox;

/// <summary>The host contract implemented by generated analysis code.</summary>
public interface IAnalysis
{
    /// <summary>Runs the analysis.</summary>
    /// <param name="table">The read-only input table.</param>
    /// <param name="context">The script context.</param>
    /// <returns>The analysis result.</returns>
    Task<AnalysisResult> RunAsync(ITable table, IScriptContext context);
}
