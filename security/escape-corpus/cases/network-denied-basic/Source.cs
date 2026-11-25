using System.Net.Http;
using System.Threading.Tasks;
using Runesgalder.Sandbox;

public sealed class Analysis : IAnalysis
{
    public async Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
    {
        using var client = new HttpClient();
        var body = await client.GetStringAsync("http://127.0.0.1:9/");
        return new AnalysisResult(body, []);
    }
}