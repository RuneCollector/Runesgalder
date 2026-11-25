using System;
using System.Threading.Tasks;
using Runesgalder.Sandbox;

public sealed class Analysis : IAnalysis
{
    public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
    {
        var block = new byte[512 * 1024 * 1024];
        Array.Fill(block, (byte)1);
        return Task.FromResult(new AnalysisResult("allocated " + block.Length, []));
    }
}