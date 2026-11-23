using System.Text.Json;
using Runesgalder.Sandbox.Compilation;
using Runesgalder.Sandbox.Worker;

namespace Runesgalder.SandboxTests;

public sealed class WorkerHostTests
{
    private const string EchoScript = """
        using System.Threading.Tasks;
        using Runesgalder.Sandbox;

        public sealed class Analysis : IAnalysis
        {
            public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
            {
                context.Log("rows=" + table.Rows.Count);
                context.Chart(new ChartSpec(ChartKind.Bar, "Counts", new[] { "a", "b" }, new[] { 1.0, 2.0 }));
                return Task.FromResult(new AnalysisResult("rows=" + table.Rows.Count, []));
            }
        }
        """;

    [Fact]
    public async Task RunsACompiledAnalysisAndWritesTheResult()
    {
        using var workspace = new TempWorkspace();
        var script = WriteScript(workspace, EchoScript);
        var data = workspace.Write("data.csv", "label,value\r\na,1\r\nb,2\r\n");
        var output = workspace.PathFor("result.json");

        var exitCode = await WorkerHost.RunAsync(new WorkerOptions(script, data, output));

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("rows=2", root.GetProperty("summary").GetString());
        Assert.Equal("rows=2", root.GetProperty("logs")[0].GetString());
        Assert.Equal("Bar", root.GetProperty("charts")[0].GetProperty("kind").GetString());
        Assert.Equal(2, root.GetProperty("charts")[0].GetProperty("values").GetArrayLength());
    }

    [Fact]
    public async Task ParsesQuotedCsvFields()
    {
        using var workspace = new TempWorkspace();
        var script = WriteScript(
            workspace,
            """
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context) =>
                    Task.FromResult(new AnalysisResult(table.Rows[0][0] + "|" + table.Rows[1][0], []));
            }
            """);
        var data = workspace.Write("data.csv", "name,value\r\n\"Smith, John\",42\r\n\"She said \"\"hi\"\"\",7\r\n");
        var output = workspace.PathFor("result.json");

        var exitCode = await WorkerHost.RunAsync(new WorkerOptions(script, data, output));

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.Equal("Smith, John|She said \"hi\"", document.RootElement.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task EnforcesTheOutputQuota()
    {
        using var workspace = new TempWorkspace();
        var script = WriteScript(
            workspace,
            """
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
                {
                    context.Log(new string('x', 64));
                    return Task.FromResult(new AnalysisResult("unreachable", []));
                }
            }
            """);
        var data = workspace.Write("data.csv", "a\r\n1\r\n");
        var output = workspace.PathFor("result.json");

        var exitCode = await WorkerHost.RunAsync(new WorkerOptions(script, data, output) { MaxOutputBytes = 16 });

        Assert.Equal(5, exitCode);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("output-quota", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task StopsAnalysisThatExceedsTheTimeout()
    {
        using var workspace = new TempWorkspace();
        var script = WriteScript(
            workspace,
            """
            using System;
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public async Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), context.CancellationToken);
                    return new AnalysisResult("unreachable", []);
                }
            }
            """);
        var data = workspace.Write("data.csv", "a\r\n1\r\n");
        var output = workspace.PathFor("result.json");

        var exitCode = await WorkerHost.RunAsync(
            new WorkerOptions(script, data, output) { Timeout = TimeSpan.FromMilliseconds(150) });

        Assert.Equal(3, exitCode);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.Equal("timeout", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RejectsATableOverTheRowLimit()
    {
        using var workspace = new TempWorkspace();
        var script = WriteScript(workspace, EchoScript);
        var data = workspace.Write("data.csv", "a\r\n1\r\n2\r\n");
        var output = workspace.PathFor("result.json");

        var exitCode = await WorkerHost.RunAsync(new WorkerOptions(script, data, output) { MaxRows = 1 });

        Assert.Equal(2, exitCode);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        Assert.Equal("input", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ProducesTheSameResultForTheSameInput()
    {
        using var workspace = new TempWorkspace();
        var script = WriteScript(
            workspace,
            """
            using System.Globalization;
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context) =>
                    Task.FromResult(new AnalysisResult(
                        context.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "|" +
                        context.NextDouble().ToString("R", CultureInfo.InvariantCulture),
                        []));
            }
            """);
        var data = workspace.Write("data.csv", "a\r\n1\r\n");

        var first = workspace.PathFor("first.json");
        var second = workspace.PathFor("second.json");
        Assert.Equal(0, await WorkerHost.RunAsync(new WorkerOptions(script, data, first)));
        Assert.Equal(0, await WorkerHost.RunAsync(new WorkerOptions(script, data, second)));

        using var firstDocument = JsonDocument.Parse(await File.ReadAllTextAsync(first));
        using var secondDocument = JsonDocument.Parse(await File.ReadAllTextAsync(second));
        Assert.Equal(
            firstDocument.RootElement.GetProperty("summary").GetString(),
            secondDocument.RootElement.GetProperty("summary").GetString());
    }

    private static string WriteScript(TempWorkspace workspace, string source)
    {
        var result = new ScriptCompiler().Compile(source);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => $"{d.Id} {d.Message}")));
        return workspace.Write("script.dll", result.Assembly!);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "runesgalder-worker-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string PathFor(string name) => Path.Combine(Root, name);

        public string Write(string name, string content) => Write(name, System.Text.Encoding.UTF8.GetBytes(content));

        public string Write(string name, byte[] content)
        {
            var path = PathFor(name);
            File.WriteAllBytes(path, content);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The OS may still hold a loaded assembly; the temp directory is disposable.
            }
        }
    }
}
