using System;
using System.Threading.Tasks;
using Runesgalder.Sandbox;

public sealed class Analysis : IAnalysis
{
    public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
    {
        var secret = Environment.GetEnvironmentVariable("RUNESGALDER_ESCAPE_SECRET") ?? string.Empty;
        return Task.FromResult(new AnalysisResult(secret.Length + "|" + Environment.CurrentDirectory, []));
    }
}