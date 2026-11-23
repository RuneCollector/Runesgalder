using System.Text.Json;
using Runesgalder.Mcp;

namespace Runesgalder.McpTests;

public sealed class ToolCatalogTests
{
    private static readonly string[] ExpectedNames =
        ["compile_csharp", "run_csharp", "get_run", "cancel_run"];

    private static readonly string[] CodeRequired = ["code"];

    private static readonly string[] RunIdRequired = ["run_id"];

    [Fact]
    public void ExposesExactlyTheFourV01Tools()
    {
        var names = SandboxTools.ListTools().Select(tool => tool.Name).ToArray();

        Assert.Equal(ExpectedNames, names);
    }

    [Fact]
    public void EverySchemaRejectsUnknownFields()
    {
        foreach (var tool in SandboxTools.ListTools())
        {
            Assert.Equal(JsonValueKind.Object, tool.InputSchema.ValueKind);
            Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
        }
    }

    [Fact]
    public void EverySchemaListsItsRequiredFields()
    {
        var tools = SandboxTools.ListTools().ToDictionary(tool => tool.Name);

        Assert.Equal(
            CodeRequired,
            tools["compile_csharp"].InputSchema.GetProperty("required")
                .EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(
            CodeRequired,
            tools["run_csharp"].InputSchema.GetProperty("required")
                .EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(
            RunIdRequired,
            tools["get_run"].InputSchema.GetProperty("required")
                .EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(
            RunIdRequired,
            tools["cancel_run"].InputSchema.GetProperty("required")
                .EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Fact]
    public void MarksOnlySideEffectFreeToolsAsReadOnly()
    {
        var tools = SandboxTools.ListTools().ToDictionary(tool => tool.Name);

        Assert.True(tools["compile_csharp"].Annotations!.ReadOnlyHint);
        Assert.True(tools["get_run"].Annotations!.ReadOnlyHint);
        Assert.False(tools["run_csharp"].Annotations!.ReadOnlyHint);
        Assert.False(tools["cancel_run"].Annotations!.ReadOnlyHint);
    }

    [Fact]
    public void RestrictsTheContractToAnalysis()
    {
        var compile = SandboxTools.ListTools().Single(tool => tool.Name == "compile_csharp");
        var contract = compile.InputSchema.GetProperty("properties").GetProperty("contract");

        Assert.Equal("analysis", contract.GetProperty("enum")[0].GetString());
    }

    [Fact]
    public void RequiresInlineCsvInsideData()
    {
        var run = SandboxTools.ListTools().Single(tool => tool.Name == "run_csharp");
        var data = run.InputSchema.GetProperty("properties").GetProperty("data");

        Assert.False(data.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("csv", data.GetProperty("required")[0].GetString());
    }

    [Fact]
    public void ReturnsAFreshListOnEveryCall()
    {
        var first = SandboxTools.ListTools();
        var second = SandboxTools.ListTools();

        Assert.NotSame(first, second);
    }
}
