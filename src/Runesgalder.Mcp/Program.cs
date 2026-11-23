using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Runesgalder.Mcp;
using Runesgalder.Sandbox.Compilation;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(console =>
    console.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(McpOptions.FromEnvironment());
builder.Services.AddSingleton(new ScriptCompiler());
builder.Services.AddSingleton(new ExecutionGate(McpLimits.QueueCapacity));
builder.Services.AddSingleton<ISandboxExecutor>(provider =>
    new ContainerSandboxExecutor(provider.GetRequiredService<McpOptions>().WorkerImage));
builder.Services.AddSingleton<RunManager>(provider =>
    new RunManager(
        provider.GetRequiredService<ScriptCompiler>(),
        provider.GetRequiredService<ISandboxExecutor>(),
        provider.GetRequiredService<ExecutionGate>(),
        provider.GetRequiredService<ILogger<RunManager>>(),
        provider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<SandboxTools>();

builder.Services
    .AddMcpServer(mcp =>
    {
        mcp.ServerInfo = new Implementation
        {
            Name = McpOptions.ServerName,
            Version = McpOptions.ServerVersion,
            Description = "Typed C# analysis execution in a hardened sandbox."
        };
        mcp.ServerInstructions =
            "Use run_csharp with C# that implements Runesgalder.Sandbox.IAnalysis (analysis/v1). " +
            "Input data is inline CSV; do not send file paths, URLs, or environment values. " +
            "Runs execute in a container with no network and may be polled with get_run.";
        mcp.Capabilities = new ServerCapabilities
        {
            Tools = new ToolsCapability()
        };
    })
    .WithStdioServerTransport()
    .WithListToolsHandler((request, _) =>
    {
        return ValueTask.FromResult(new ListToolsResult
        {
            Tools = SandboxTools.ListTools()
        });
    })
    .WithCallToolHandler((request, cancellationToken) =>
    {
        var tools = (request.Services ?? throw new InvalidOperationException(
                "The MCP request has no service provider."))
            .GetRequiredService<SandboxTools>();
        return new ValueTask<CallToolResult>(tools.CallAsync(
            request.Params.Name,
            request.Params.Arguments,
            cancellationToken));
    });

var app = builder.Build();
await app.RunAsync();
