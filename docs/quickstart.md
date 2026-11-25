# Quickstart

This guide builds the repository, runs the demos, starts the local sandbox image, and connects an MCP client. It takes about ten minutes on a machine with a working .NET SDK and Docker.

> Runesgalder is pre-alpha. The sandbox is a local, single-tenant container boundary, not a hardened multi-tenant service. Do not run code you would not run on your own machine.

## Prerequisites

- .NET SDK 10.0.401 or later in the 10.0 feature band (`dotnet --version`).
- Git.
- Docker with Linux containers. On Windows, install Docker Desktop with the WSL2 backend. On macOS and Linux, Docker Desktop or Docker Engine is enough.
- For MCP clients: Claude Code, Cursor, or any client that can launch a stdio server.

Check Docker from the same shell you will use below:

```bash
docker version --format '{{.Server.Os}}'
```

The output must be `linux`. If it is `windows`, switch Docker Desktop to Linux containers.

## 1. Build and test

```bash
git clone https://github.com/Runesgalder/Runesgalder.git
cd Runesgalder
dotnet build Runesgalder.sln -c Release
dotnet test Runesgalder.sln -c Release
```

Tests that need Docker and the worker image are skipped on a host that has neither. On Linux with the image built, they run instead.

## 2. Run the CLI demos

```bash
dotnet run --project src/Runesgalder.Cli -c Release -- demo
dotnet run --project src/Runesgalder.Cli -c Release -- doctor
```

`demo` runs a three-step typed flow, prints its journal, and shows the default sandbox policy (network denied, bounded memory and wall clock). `doctor` prints the runtime, platform, container detection, and whether the host can run the Linux sandbox.

`resume` shows checkpoint reuse:

```bash
dotnet run --project src/Runesgalder.Cli -c Release -- resume
```

## 3. Start the sandbox image in one command

From the repository root:

```bash
bash scripts/sandbox-up.sh
```

On Windows PowerShell:

```powershell
.\scripts\sandbox-up.ps1
```

Each script publishes the worker, builds the `runesgalder-worker:dev` image, and verifies that the tag exists. The container runs with no network, a read-only root filesystem, no Linux capabilities, and no new privileges.

If you prefer to run the steps by hand:

```bash
dotnet publish src/Runesgalder.Sandbox.Worker/Runesgalder.Sandbox.Worker.csproj -c Release -o artifacts/worker
docker build -f src/Runesgalder.Sandbox.Worker/Dockerfile -t runesgalder-worker:dev artifacts/worker
```

## 4. Build and connect the MCP server

```bash
dotnet publish src/Runesgalder.Mcp/Runesgalder.Mcp.csproj -c Release -o artifacts/mcp
```

Add the server to your client configuration. Use an absolute path:

```json
{
  "mcpServers": {
    "runesgalder": {
      "command": "/absolute/path/to/artifacts/mcp/runesgalder-mcp"
    }
  }
}
```

On Windows the command is `runesgalder-mcp.exe`. In Claude Code you can add it with:

```bash
claude mcp add runesgalder -- /absolute/path/to/artifacts/mcp/runesgalder-mcp
```

Optional environment variables:

- `RUNESGALDER_MCP_WORKER_IMAGE` — worker image, default `runesgalder-worker:dev`.
- `RUNESGALDER_MCP_MAX_WALL_CLOCK_SECONDS` — operator ceiling, default and maximum 120.

## 5. Call a tool

Ask the client to call `compile_csharp` with a class that does not implement the analysis contract:

```json
{"code": "public sealed class Analysis { }"}
```

The result is a tool error with code `contract_invalid`. That proves the handshake, tool listing, and compile path. A valid script implements `Runesgalder.Sandbox.IAnalysis`:

```csharp
using System.Threading.Tasks;
using Runesgalder.Sandbox;

public sealed class Analysis : IAnalysis
{
    public Task<AnalysisResult> RunAsync(ITable table, IScriptContext context)
    {
        context.Log("rows: " + table.Rows.Count);
        return Task.FromResult(new AnalysisResult("ok", []));
    }
}
```

`run_csharp` takes the same `code` plus inline CSV as `data`. `get_run` polls a run by `run_id`. `cancel_run` is cooperative: it returns the current envelope, which may still be `running`; poll `get_run` until the status is terminal.

## Troubleshooting

**`doctor` says the sandbox host is not Linux.** The container path needs a Linux Docker daemon. On Windows, enable the WSL2 backend in Docker Desktop and run the commands from WSL2 or PowerShell with Docker Desktop running.

**Docker-gated tests are skipped.** Either Docker is not running, it is serving Windows containers, or the `runesgalder-worker:dev` image is missing. Run `scripts/sandbox-up.sh` and try again.

**`sandbox_unavailable` from `run_csharp`.** The server could not start the container. Check `docker image inspect runesgalder-worker:dev`, that the Docker daemon is reachable by the process that launched the server, and that the image name matches `RUNESGALDER_MCP_WORKER_IMAGE`.

**The server exits immediately.** Run `dotnet artifacts/mcp/runesgalder-mcp.dll` in a terminal. Logs go to stderr; stdout is reserved for protocol frames. A missing worker image does not stop the server from starting, but it does stop runs.

**Claude Code reports an unavailable model.** That is the client's own model configuration, not the server. Pick a model the client can use and retry.

## What is not ready

- The Linux daemon, remote transport, and strong isolation tiers are not implemented.
- Windows native isolation is deferred; use WSL2 or Docker.
- Cross-process trace context over stdio is not carried in v0.1.
- No NuGet packages are published. Build from source.

See the [project plan](PROJECT_PLAN.md) for scope and the [threat model](../security/threat-model.md) for residual risk. Security reports go to `3070687583@qq.com` only, per [SECURITY.md](../SECURITY.md).

Other languages: [中文](quickstart.zh-CN.md) · [Deutsch](quickstart.de.md)