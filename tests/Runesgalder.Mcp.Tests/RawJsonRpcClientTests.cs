using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Runesgalder.McpTests;

/// <summary>
/// A second, independent MCP client: hand-written JSON-RPC over the server's stdio transport,
/// with no use of the ModelContextProtocol client library.
/// </summary>
public sealed class RawJsonRpcClientTests
{
    private static readonly string[] ExpectedNames =
        ["compile_csharp", "run_csharp", "get_run", "cancel_run"];

    [Fact]
    public async Task AHandWrittenJsonRpcClientCanUseTheServer()
    {
        var serverPath = Path.Combine(AppContext.BaseDirectory, "runesgalder-mcp.dll");
        Assert.True(File.Exists(serverPath), $"The server assembly was not copied to '{serverPath}'.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var client = RawStdioClient.Start(serverPath);

        using var initialize = await client.RequestAsync(
            "initialize",
            new
            {
                protocolVersion = "2025-06-18",
                capabilities = new { },
                clientInfo = new { name = "runesgalder-raw-jsonrpc", version = "0.0.1" }
            },
            timeout.Token);

        var result = initialize.RootElement.GetProperty("result");
        Assert.Equal("runesgalder", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal("0.1.0-preview.0", result.GetProperty("serverInfo").GetProperty("version").GetString());
        Assert.Contains(
            "analysis/v1",
            result.GetProperty("instructions").GetString(),
            StringComparison.Ordinal);
        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("protocolVersion").GetString()));

        await client.NotifyAsync("notifications/initialized", timeout.Token);

        using var listed = await client.RequestAsync("tools/list", new { }, timeout.Token);
        var tools = listed.RootElement.GetProperty("result").GetProperty("tools");
        Assert.Equal(
            ExpectedNames,
            tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToArray());
        foreach (var tool in tools.EnumerateArray())
        {
            var schema = tool.GetProperty("inputSchema");
            Assert.Equal(JsonValueKind.Object, schema.ValueKind);
            Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        }

        using var contract = await client.RequestAsync(
            "tools/call",
            new Dictionary<string, object?>
            {
                ["name"] = "compile_csharp",
                ["arguments"] = new Dictionary<string, object?>
                {
                    ["code"] = "public sealed class Analysis { }"
                }
            },
            timeout.Token);
        var contractResult = contract.RootElement.GetProperty("result");
        Assert.True(contractResult.GetProperty("isError").GetBoolean());
        var contractText = contractResult.GetProperty("content")[0].GetProperty("text").GetString()!;
        using var contractPayload = JsonDocument.Parse(contractText);
        Assert.Equal("contract_invalid", ErrorCode(contractPayload.RootElement));

        using var unknown = await client.RequestAsync(
            "tools/call",
            new Dictionary<string, object?>
            {
                ["name"] = "not_a_tool",
                ["arguments"] = new Dictionary<string, object?>()
            },
            timeout.Token);
        var unknownResult = unknown.RootElement.GetProperty("result");
        Assert.True(unknownResult.GetProperty("isError").GetBoolean());
        var unknownText = unknownResult.GetProperty("content")[0].GetProperty("text").GetString()!;
        using var unknownPayload = JsonDocument.Parse(unknownText);
        Assert.Equal("invalid_request", ErrorCode(unknownPayload.RootElement));

        Assert.DoesNotContain(
            client.ErrorLines,
            line => line.Contains("Unhandled exception", StringComparison.Ordinal));
    }

    private static string? ErrorCode(JsonElement payload)
    {
        var error = payload.TryGetProperty("error", out var nested) ? nested : payload;
        return error.GetProperty("code").GetString();
    }

    /// <summary>Newline-delimited JSON-RPC 2.0 over the server's stdin and stdout.</summary>
    private sealed class RawStdioClient : IAsyncDisposable
    {
        private readonly Process process;
        private readonly StreamWriter input;
        private readonly StreamReader output;
        private int nextId;

        private RawStdioClient(Process process)
        {
            this.process = process;
            input = process.StandardInput;
            input.NewLine = "\n";
            input.AutoFlush = true;
            output = process.StandardOutput;
        }

        public ConcurrentQueue<string> ErrorLines { get; } = new();

        public static RawStdioClient Start(string serverPath)
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            startInfo.ArgumentList.Add(serverPath);

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The MCP server process could not be started.");
            var client = new RawStdioClient(process);
            _ = client.DrainErrorsAsync();
            return client;
        }

        public async Task NotifyAsync(string method, CancellationToken cancellationToken)
        {
            var notification = new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method
            };
            var line = JsonSerializer.Serialize(notification);
            await input.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        public async Task<JsonDocument> RequestAsync(
            string method,
            object parameters,
            CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref nextId);
            var request = new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters
            };
            var line = JsonSerializer.Serialize(request);
            await input.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);

            while (true)
            {
                var response = await output.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        "The server closed stdout. stderr: " + string.Join(" | ", ErrorLines));
                if (string.IsNullOrWhiteSpace(response))
                {
                    continue;
                }

                var document = JsonDocument.Parse(response);
                if (document.RootElement.TryGetProperty("id", out var responseId)
                    && responseId.ValueKind == JsonValueKind.Number
                    && responseId.GetInt32() == id)
                {
                    return document;
                }

                document.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                input.Close();
            }
            catch (IOException)
            {
                // The server may already have exited.
            }

            try
            {
                if (!process.WaitForExit(TimeSpan.FromSeconds(5)))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            finally
            {
                process.Dispose();
            }

            await Task.CompletedTask;
        }

        private async Task DrainErrorsAsync()
        {
            while (await outputErrorLineAsync().ConfigureAwait(false) is { } line)
            {
                ErrorLines.Enqueue(line);
            }

            async Task<string?> outputErrorLineAsync() =>
                await process.StandardError.ReadLineAsync().ConfigureAwait(false);
        }
    }
}
