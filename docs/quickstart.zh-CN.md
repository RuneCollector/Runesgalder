# 快速开始

本指南带你构建仓库、运行演示、启动本地沙箱镜像，并接入一个 MCP 客户端。在已安装 .NET SDK 与 Docker 的机器上大约需要十分钟。

> Runesgalder 处于 pre-alpha 阶段。沙箱是本地、单租户的容器边界，不是经过加固的多租户服务。不要运行你在自己机器上都不敢运行的代码。

## 前置条件

- .NET SDK 10.0.401 或同属 10.0 功能带的更高版本（`dotnet --version`）。
- Git。
- 支持 Linux 容器的 Docker。Windows 上安装使用 WSL2 后端的 Docker Desktop；macOS 与 Linux 上 Docker Desktop 或 Docker Engine 均可。
- MCP 客户端：Claude Code、Cursor，或任何能启动 stdio 服务器的客户端。

在接下来要使用的同一个终端里确认 Docker：

```bash
docker version --format '{{.Server.Os}}'
```

输出必须是 `linux`。如果输出是 `windows`，请在 Docker Desktop 中切换到 Linux 容器。

## 1. 构建与测试

```bash
git clone https://github.com/Runesgalder/Runesgalder.git
cd Runesgalder
dotnet build Runesgalder.sln -c Release
dotnet test Runesgalder.sln -c Release
```

依赖 Docker 与 worker 镜像的测试会在两者都不具备的主机上跳过；在已构建镜像的 Linux 主机上则会实际运行。

## 2. 运行 CLI 演示

```bash
dotnet run --project src/Runesgalder.Cli -c Release -- demo
dotnet run --project src/Runesgalder.Cli -c Release -- doctor
```

`demo` 运行一个三步的类型化 Flow，打印日志，并显示默认沙箱策略（拒绝网络、内存与墙钟受限）。`doctor` 打印运行时、平台、容器检测结果，以及当前主机能否运行 Linux 沙箱。

`resume` 演示检查点复用：

```bash
dotnet run --project src/Runesgalder.Cli -c Release -- resume
```

## 3. 一条命令启动沙箱镜像

在仓库根目录执行：

```bash
bash scripts/sandbox-up.sh
```

Windows PowerShell:

```powershell
.\scripts\sandbox-up.ps1
```

脚本会发布 worker、构建 `runesgalder-worker:dev` 镜像并校验标签存在。容器以无网络、只读根文件系统、丢弃全部 Linux capability、禁止提权的方式运行。

如果希望手动执行：

```bash
dotnet publish src/Runesgalder.Sandbox.Worker/Runesgalder.Sandbox.Worker.csproj -c Release -o artifacts/worker
docker build -f src/Runesgalder.Sandbox.Worker/Dockerfile -t runesgalder-worker:dev artifacts/worker
```

## 4. 构建并接入 MCP 服务器

```bash
dotnet publish src/Runesgalder.Mcp/Runesgalder.Mcp.csproj -c Release -o artifacts/mcp
```

把服务器加入客户端配置，使用绝对路径：

```json
{
  "mcpServers": {
    "runesgalder": {
      "command": "/absolute/path/to/artifacts/mcp/runesgalder-mcp"
    }
  }
}
```

Windows 上命令为 `runesgalder-mcp.exe`。在 Claude Code 中可以直接添加：

```bash
claude mcp add runesgalder -- /absolute/path/to/artifacts/mcp/runesgalder-mcp
```

可选环境变量：

- `RUNESGALDER_MCP_WORKER_IMAGE` —— worker 镜像名，默认 `runesgalder-worker:dev`。
- `RUNESGALDER_MCP_MAX_WALL_CLOCK_SECONDS` —— 运维侧墙钟上限，默认且最大为 120。

## 5. 调用工具

让客户端调用 `compile_csharp`，传入一个未实现分析契约的类：

```json
{"code": "public sealed class Analysis { }"}
```

返回应为一个工具错误，错误码为 `contract_invalid`。这证明握手、工具列表与编译链路都通了。有效脚本需要实现 `Runesgalder.Sandbox.IAnalysis`：

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

`run_csharp` 接收同样的 `code`，并通过 `data` 传入内联 CSV。`get_run` 按 `run_id` 轮询。`cancel_run` 是协作式的：它返回当前信封，状态可能仍是 `running`，需要继续轮询 `get_run` 直到终态。

## 常见问题

**`doctor` 显示沙箱主机不是 Linux。** 容器路径需要 Linux Docker 守护进程。Windows 上请在 Docker Desktop 启用 WSL2 后端，并在 WSL2 或 PowerShell 中运行命令。

**依赖 Docker 的测试被跳过。** Docker 未运行、只提供 Windows 容器，或缺少 `runesgalder-worker:dev` 镜像。运行 `scripts/sandbox-up.sh` 后重试。

**`run_csharp` 返回 `sandbox_unavailable`。** 服务器无法启动容器。检查 `docker image inspect runesgalder-worker:dev`、启动服务器的进程能否访问 Docker 守护进程，以及镜像名是否与 `RUNESGALDER_MCP_WORKER_IMAGE` 一致。

**服务器立即退出。** 在终端直接运行 `dotnet artifacts/mcp/runesgalder-mcp.dll`。日志输出到 stderr，stdout 只用于协议帧。缺少镜像不会阻止服务器启动，但会导致运行失败。

**Claude Code 提示模型不可用。** 这是客户端自身的模型配置问题，与服务器无关。换一个客户端可用的模型后重试。

## 尚未就绪的部分

- Linux 守护进程、远程传输与更强隔离等级尚未实现。
- Windows 原生隔离推迟；请使用 WSL2 或 Docker。
- v0.1 不通过 stdio 传递跨进程追踪上下文。
- 尚未发布任何 NuGet 包，请从源码构建。

范围见[项目计划](PROJECT_PLAN.md)，残余风险见[威胁模型](../security/threat-model.md)。安全问题只发送到 `3070687583@qq.com`，见 [SECURITY.md](../SECURITY.md)。

其他语言：[English](quickstart.md) · [Deutsch](quickstart.de.md)