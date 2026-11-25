using System.Threading.Tasks;
using Runesgalder.Sandbox;

public sealed class Analysis : IAnalysis
{
    public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
    {
        context.Log(new string('x', 4 * 1024 * 1024));
        return Task.FromResult(new AnalysisResult("unreachable", []));
    }
}