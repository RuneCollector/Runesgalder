using System.Text.Json;
using ModelContextProtocol.Protocol;
using Runesgalder.Mcp;

namespace Runesgalder.McpTests;

public sealed class CompileToolTests
{
    [Fact]
    public async Task CompilesAValidScriptIntoAnArtifact()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "compile_csharp",
            Harness.Args(new { code = Harness.ValidScript }),
            CancellationToken.None);

        Assert.False(result.IsError);
        var payload = Harness.Structured(result);
        Assert.True(payload.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("error").ValueKind);
        Assert.Equal(0, payload.GetProperty("diagnostics").GetArrayLength());

        var artifact = payload.GetProperty("artifact");
        Assert.Equal(64, artifact.GetProperty("sha256").GetString()!.Length);
        Assert.True(artifact.GetProperty("bytes").GetInt32() > 0);
        Assert.True(payload.GetProperty("duration_ms").GetDouble() >= 0);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var textDocument = JsonDocument.Parse(text.Text);
        Assert.True(textDocument.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task ReportsContractDiagnosticsAndTheContractError()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "compile_csharp",
            Harness.Args(new { code = "public sealed class Analysis { }" }),
            CancellationToken.None);

        Assert.True(result.IsError);
        var payload = Harness.Structured(result);
        Assert.False(payload.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("artifact").ValueKind);
        Assert.Equal("contract_invalid", Harness.ErrorCode(result));
        Assert.Contains(
            payload.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("id").GetString() == "RGS0002" &&
                diagnostic.GetProperty("severity").GetString() == "error");
    }

    [Fact]
    public async Task ReportsPolicyDeniedForABannedNamespace()
    {
        const string source = """
            using System.IO;
            using System.Threading.Tasks;
            using Runesgalder.Sandbox;

            public sealed class Analysis : IAnalysis
            {
                public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context) =>
                    Task.FromResult(new AnalysisResult("rows", []));
            }
            """;

        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "compile_csharp",
            Harness.Args(new { code = source }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("policy_denied", Harness.ErrorCode(result));
    }

    [Fact]
    public async Task RejectsACompileCallWithAnUnsupportedContract()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "compile_csharp",
            Harness.Args(new { code = Harness.ValidScript, contract = "chat" }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("contract_invalid", Harness.ErrorCode(result));
    }

    [Fact]
    public async Task RejectsUnknownFields()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "compile_csharp",
            Harness.Args(new { code = Harness.ValidScript, file = "a.cs" }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("invalid_request", Harness.ErrorCode(result));
        Assert.Contains("file", Harness.Structured(result).GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task RejectsAMissingCodeField()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "compile_csharp",
            Harness.Args(new { contract = "analysis" }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("invalid_request", Harness.ErrorCode(result));
    }

    [Fact]
    public async Task RejectsOversizedSource()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync(
            "compile_csharp",
            Harness.Args(new { code = new string(' ', McpLimits.MaxSourceBytes + 1) }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("source_too_large", Harness.ErrorCode(result));
    }

    [Fact]
    public async Task ReturnsServerBusyWhenEverySlotIsReserved()
    {
        await using var harness = new Harness(gateCapacity: 1);
        Assert.True(harness.Gate.TryReserve(out var slot));
        try
        {
            var result = await harness.Tools.CallAsync(
                "compile_csharp",
                Harness.Args(new { code = Harness.ValidScript }),
                CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("server_busy", Harness.ErrorCode(result));
            Assert.False(Harness.Structured(result).GetProperty("ok").GetBoolean());
        }
        finally
        {
            slot!.Dispose();
        }
    }

    [Fact]
    public async Task RejectsAnUnknownToolName()
    {
        await using var harness = new Harness();

        var result = await harness.Tools.CallAsync("compile_python", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("invalid_request", Harness.ErrorCode(result));
    }
}
