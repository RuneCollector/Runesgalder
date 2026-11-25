using System.IO;
using System.Threading.Tasks;
using Runesgalder.Sandbox;

public sealed class Analysis : IAnalysis
{
    public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context) =>
        Task.FromResult(new AnalysisResult(File.ReadAllText("/etc/passwd"), []));
}