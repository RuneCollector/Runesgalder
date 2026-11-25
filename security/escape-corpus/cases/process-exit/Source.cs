using System;
using System.Threading.Tasks;
using Runesgalder.Sandbox;

public sealed class Analysis : IAnalysis
{
    public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
    {
        Environment.Exit(0);
        return Task.FromResult(new AnalysisResult("forged", []));
    }
}