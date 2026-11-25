using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Runesgalder.Sandbox;

public sealed class Analysis : IAnalysis
{
    [DllImport("libc")]
    private static extern int getpid();

    public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context) =>
        Task.FromResult(new AnalysisResult(getpid().ToString(), []));
}