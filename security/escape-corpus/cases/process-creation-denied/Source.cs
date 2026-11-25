using System.Diagnostics;
using System.Threading.Tasks;
using Runesgalder.Sandbox;

public sealed class Analysis : IAnalysis
{
    public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
    {
        Process.Start("/bin/true");
        return Task.FromResult(new AnalysisResult("started", []));
    }
}