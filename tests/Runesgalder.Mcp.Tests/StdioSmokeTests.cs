using System.Collections.Concurrent;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Runesgalder.McpTests;

public sealed class StdioSmokeTests
{
    private static readonly string[] ExpectedNames =
        ["compile_csharp", "run_csharp", "get_run", "cancel_run"];

    [Fact]
    public async Task ServesThePublishedContractOverStdio()
    {
        var serverPath = Path.Combine(AppContext.BaseDirectory, "runesgalder-mcp.dll");
        Assert.True(
            File.Exists(serverPath),
            $"The server assembly was not copied to '{serverPath}'.");

        var errorLines = new ConcurrentQueue<string>();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "runesgalder-smoke",
            Command = "dotnet",
            Arguments = [serverPath],
            InheritEnvironmentVariables = true,
            StandardErrorLines = errorLines.Enqueue
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: timeout.Token);

        Assert.NotNull(client.ServerInfo);
        Assert.Equal("runesgalder", client.ServerInfo.Name);
        Assert.Equal("0.1.0-preview.0", client.ServerInfo.Version);
        Assert.NotNull(client.ServerInstructions);
        Assert.Contains("analysis/v1", client.ServerInstructions, StringComparison.Ordinal);
        Assert.NotNull(client.ServerCapabilities.Tools);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(ExpectedNames, tools.Select(tool => tool.Name).ToArray());
        foreach (var tool in tools)
        {
            Assert.Equal(JsonValueKind.Object, tool.JsonSchema.ValueKind);
            Assert.False(tool.JsonSchema.GetProperty("additionalProperties").GetBoolean());
        }

        var contract = await client.CallToolAsync(
            "compile_csharp",
            new Dictionary<string, object?> { ["code"] = "public sealed class Analysis { }" },
            cancellationToken: timeout.Token);
        Assert.True(contract.IsError);
        Assert.Equal("contract_invalid", ErrorCode(contract));

        var missingField = await client.CallToolAsync(
            "compile_csharp",
            new Dictionary<string, object?>(),
            cancellationToken: timeout.Token);
        Assert.True(missingField.IsError);
        Assert.Equal("invalid_request", ErrorCode(missingField));

        var notFound = await client.CallToolAsync(
            "get_run",
            new Dictionary<string, object?> { ["run_id"] = "smoke-missing-run" },
            cancellationToken: timeout.Token);
        Assert.True(notFound.IsError);
        Assert.Equal("run_not_found", ErrorCode(notFound));

        var unknownTool = await client.CallToolAsync(
            "not_a_tool",
            new Dictionary<string, object?>(),
            cancellationToken: timeout.Token);
        Assert.True(unknownTool.IsError);
        Assert.Equal("invalid_request", ErrorCode(unknownTool));

        Assert.DoesNotContain(
            errorLines,
            line => line.Contains("Unhandled exception", StringComparison.Ordinal));
    }

    private static string? ErrorCode(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var document = JsonDocument.Parse(text.Text);
        var error = document.RootElement.TryGetProperty("error", out var nested)
            ? nested
            : document.RootElement;
        return error.GetProperty("code").GetString();
    }
}
