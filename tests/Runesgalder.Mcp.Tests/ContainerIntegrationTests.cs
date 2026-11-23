using Runesgalder.Mcp;

namespace Runesgalder.McpTests;

public sealed class ContainerIntegrationTests
{
    private const string Script = """
        using System.Threading.Tasks;
        using Runesgalder.Sandbox;

        public sealed class Analysis : IAnalysis
        {
            public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
            {
                context.Log(table.Name);
                return Task.FromResult(new AnalysisResult($"{table.Rows.Count} rows", []));
            }
        }
        """;

    [DockerFact]
    public async Task ExecutesAnAnalysisWithInlineCsv()
    {
        await using var harness = CreateHarness();

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new
            {
                code = Script,
                data = new { name = "sales", csv = "region,amount\nnorth,1\nsouth,2\n" },
                wait_seconds = 90,
                wall_clock_seconds = 60
            }),
            CancellationToken.None);

        Assert.False(result.IsError);
        var payload = Harness.Structured(result);
        Assert.Equal(RunStatus.Succeeded, payload.GetProperty("status").GetString());
        var analysis = payload.GetProperty("result");
        Assert.Equal("2 rows", analysis.GetProperty("summary").GetString());
        Assert.Contains(
            analysis.GetProperty("logs").EnumerateArray(),
            line => line.GetString() == "data");
        Assert.True(payload.GetProperty("resource").GetProperty("output_bytes").GetInt64() > 0);
    }

    [DockerFact]
    public async Task TreatsOmittedDataAsAnEmptyTable()
    {
        await using var harness = CreateHarness();

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new
            {
                code = Script,
                wait_seconds = 90,
                wall_clock_seconds = 60
            }),
            CancellationToken.None);

        Assert.False(result.IsError);
        var payload = Harness.Structured(result);
        Assert.Equal(RunStatus.Succeeded, payload.GetProperty("status").GetString());
        Assert.Equal(
            "0 rows",
            payload.GetProperty("result").GetProperty("summary").GetString());
    }

    [DockerFact]
    public async Task ReportsAScriptFailureWithoutLeakingHostPaths()
    {
        const string failing = """
            using System;
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context) =>
                    throw new InvalidOperationException("boom");
            }
            """;

        await using var harness = CreateHarness();

        var result = await harness.Tools.CallAsync(
            "run_csharp",
            Harness.Args(new
            {
                code = failing,
                wait_seconds = 90,
                wall_clock_seconds = 60
            }),
            CancellationToken.None);

        Assert.True(result.IsError);
        var payload = Harness.Structured(result);
        Assert.Equal(RunStatus.Failed, payload.GetProperty("status").GetString());
        Assert.Equal("internal", Harness.ErrorCode(result));
        var message = payload.GetProperty("error").GetProperty("message").GetString();
        Assert.Contains("boom", message);
        Assert.DoesNotContain("runesgalder-mcp", message, StringComparison.OrdinalIgnoreCase);
    }

    private static Harness CreateHarness() =>
        new(executor: new ContainerSandboxExecutor("runesgalder-worker:dev"));
}
