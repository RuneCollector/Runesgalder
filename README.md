<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/brand/crest-dark-1024.png">
    <img src="assets/brand/crest-1024.png" alt="Runesgalder: an Algiz stave between two four-feathered wings, with an ember diamond beneath the foot" width="170">
  </picture>
</p>

# Runesgalder

A governed runtime for agent workflows and safe C# code execution.

Runesgalder is a pre-alpha .NET project with two first-class surfaces over one runtime:

- **Flow** — typed agent orchestration with LINQ-style composition, a typed event journal, retries, cancellation, and a stream of run events. A durable journal is planned; traces and metrics are instrumented for host exporters.
- **Sandbox (in development)** — a deny-by-default execution boundary for untrusted, AI-generated C# code. Generated code is designed to receive host-defined contracts, not arbitrary files, network access, or package installation.

Both surfaces are designed around the same primitives: `Step`, `Effect`, `Policy`, `Journal`, `Artifact`, and `RunEvent`. The design goal is that every effect is governed.

Project site: <https://runesgalder.github.io/>

Created and maintained by [@karlmoriaty](https://github.com/karlmoriaty), with contributions from the community. See [AUTHORS](AUTHORS) for ownership and [MAINTAINERS.md](MAINTAINERS.md) for the current team and co-maintainer openings.

## Status

**Pre-alpha. Not production ready. No packages have been published.**

| Area | State |
| --- | --- |
| Typed Flow steps, journal events, retry, cancellation | Working skeleton |
| Sandbox policy contracts and default-deny policy | Working skeleton |
| Roslyn compile pipeline and container worker | Working; Linux `sandboxd` not started |
| MCP server over stdio (`compile_csharp`, `run_csharp`, `get_run`, `cancel_run`) | Working skeleton; analysis contract only |
| Sandbox and MCP traces and metrics | Implemented; no SDK or exporter in the library |
| English, Chinese and German Quickstarts | Available |
| GitHub Actions CI (build, test, format, Linux container E2E) | Present |
| CodeQL, Dependabot, issue and pull-request templates | Present |
| Threat model and escape corpus | Present; corpus populated and Docker-gated |

The v0.1 target is a Linux sandbox daemon with container isolation and a cross-platform client. Windows native strong isolation is deferred; development on Windows uses WSL2 or Docker.

## Quick start

Prerequisites:

- .NET SDK 10.0.401 or later in the 10.0 feature band
- Git
- Docker with Linux containers for the sandbox image (WSL2 backend on Windows)

```bash
dotnet build Runesgalder.sln -c Release
dotnet test Runesgalder.sln -c Release
dotnet run --project src/Runesgalder.Cli -c Release -- demo
dotnet run --project src/Runesgalder.Cli -c Release -- doctor
bash scripts/sandbox-up.sh
```

`doctor` prints the runtime, platform, container detection, and whether the current host can run the Linux sandbox. `scripts/sandbox-up.sh` publishes the worker and builds the `runesgalder-worker:dev` image in one step; on Windows use `scripts/sandbox-up.ps1`.

The full walkthrough, including MCP client configuration and troubleshooting, is in the [Quickstart](docs/quickstart.md) ([中文](docs/quickstart.zh-CN.md) · [Deutsch](docs/quickstart.de.md)).

## A small Flow example

```csharp
using System.Globalization;
using Runesgalder;

var flow = Flow.Start(
        Step.Define<string, int>(
            "parse",
            input => int.Parse(input, CultureInfo.InvariantCulture)))
    .Then(Step.Define<int, int>("double", value => value * 2))
    .Then(Step.Define<int, string>(
        "format",
        value => $"Result: {value.ToString(CultureInfo.InvariantCulture)}"));

var journal = new InMemoryJournal();
var result = await flow.RunAsync("21", journal);

Console.WriteLine(result);
```

The in-memory journal records `RunStarted`, `StepStarted`, `StepCompleted`, `RunCompleted`, failures, and retry scheduling. A full run is a stream of typed events, not an unstructured log. A durable journal store is planned.

## Sandbox policy example

```csharp
using Runesgalder.Sandbox;

var policy = SandboxPolicy.CreateBuilder()
    .AddReadOnlyFile("data/sales.csv")
    .Build();
```

The default policy denies network access, uses bounded memory, CPU, process, output, and wall-clock limits, and enables deterministic clock and random sources. Generated analysis code is expected to receive host-provided `ITable` and `IScriptContext` objects. It must not receive arbitrary host paths or credentials.

The sandbox engine is still under construction. Treat the current repository as a design and skeleton, not as a secure execution boundary.

## MCP server

`src/Runesgalder.Mcp` exposes the sandbox to any MCP client over stdio. It is not published as a package yet; build it from source:

```bash
dotnet publish src/Runesgalder.Sandbox.Worker/Runesgalder.Sandbox.Worker.csproj -c Release -o artifacts/worker
docker build -f src/Runesgalder.Sandbox.Worker/Dockerfile -t runesgalder-worker:dev artifacts/worker
dotnet publish src/Runesgalder.Mcp/Runesgalder.Mcp.csproj -c Release -o artifacts/mcp
```

Client configuration (Claude Code, Cursor, or another stdio client):

```json
{
  "mcpServers": {
    "runesgalder": {
      "command": "/absolute/path/to/artifacts/mcp/runesgalder-mcp.exe"
    }
  }
}
```

On Linux use `runesgalder-mcp` without the `.exe` suffix. Environment variables:

- `RUNESGALDER_MCP_WORKER_IMAGE` — worker image name, default `runesgalder-worker:dev`.
- `RUNESGALDER_MCP_MAX_WALL_CLOCK_SECONDS` — operator wall-clock ceiling, default and maximum 120.

The four tools are `compile_csharp`, `run_csharp`, `get_run`, and `cancel_run`. v0.1 executes the analysis contract only: generated code implements `Runesgalder.Sandbox.IAnalysis`, input data is inline CSV, and no file paths, URLs, environment values, or Docker options are accepted. Runs execute in the hardened container; one runs at a time and four may queue. `cancel_run` is cooperative: it returns the current envelope, which may still be `running`, and the run reaches `cancelled` once cancellation or the container deadline takes effect, so poll `get_run`. stdout carries MCP frames only; logs go to stderr. The server needs Docker on the host and no network listener. The stdio surface is tested by two independent client implementations in `tests/Runesgalder.Mcp.Tests`: the official Model Context Protocol client library and a hand-written JSON-RPC client. See [ADR-0008](docs/adr/0008-mcp-surface.md) for the full contract.

## Security warning

Do not run untrusted code with this repository yet. The sandbox implementation, Linux daemon, container hardening, and escape test corpus are not complete. The v0.1 isolation model is local, single-tenant, and container-based; it is not a claim of multi-tenant hardening.

Security reports should follow [SECURITY.md](SECURITY.md). The only security contact is `3070687583@qq.com`.

Runesgalder is independent and is not affiliated with or endorsed by Microsoft.

## Design documents

- [Project plan](docs/PROJECT_PLAN.md) — scope, v0.1 budget, delivery gates, and risks.
- [Quickstart](docs/quickstart.md) — build, demos, the one-command sandbox image, and MCP client setup ([中文](docs/quickstart.zh-CN.md) · [Deutsch](docs/quickstart.de.md)).
- [Brand](docs/brand.md) — the mark, colour, clear space, and where each asset is used.
- [Architecture decisions](docs/adr/README.md) — the decisions behind the unified runtime, equality of the two surfaces, Linux-first delivery, isolation tiers, and Native AOT roles.
- [Observability](docs/observability.md) — the trace and metric contract, host enablement, and the local-only default.
- [Threat model](security/threat-model.md) — assets, trust boundaries, threats, mitigations, and residual risk.
- [Preliminary name check](docs/name-check.md) — engineering search results, not legal or trademark clearance.
- [Demo log](docs/demos/) — daily evidence.
- [Acceptance log](docs/acceptance/) — what was accepted, by whom, and with what evidence.

## Repository layout

```text
.github/                          CI, CodeQL, Dependabot, issue and PR templates
assets/brand/                     Crest, lockups, app icons, social header
src/
  Runesgalder.Abstractions/        Shared runtime primitives
  Runesgalder.Flow/                Typed flow orchestration
  Runesgalder.Sandbox.Abstractions/ Host contracts and sandbox policy
  Runesgalder.Sandbox.Compilation/ Roslyn compile pipeline
  Runesgalder.Sandbox.Worker/      Sandbox worker and container image
  Runesgalder.Sandbox.Containers/  Container runner with hard deadline
  Runesgalder.Mcp/                 MCP server over stdio
  Runesgalder.Cli/                 Development CLI
tests/
  Runesgalder.Flow.Tests/
  Runesgalder.Sandbox.Tests/
  Runesgalder.Mcp.Tests/
docs/
  PROJECT_PLAN.md
  adr/
  demos/
  acceptance/
security/
  threat-model.md
  escape-corpus/
```

## Roadmap

The v0.1-preview scope is deliberately narrow:

- Flow: exactly seven combinators — `Then`, `Branch`, `FanOut`/`FanIn`, `Retry`, `Timeout`, `Fallback`, and `Checkpoint`.
- Sandbox: one Linux isolation backend, one journal store, host-defined contracts, deterministic mode.
- MCP server over stdio.
- The data-analysis code-interpreter demo: CSV plus a question, generated typed C#, compile diagnostics, sandboxed execution, and an SVG chart.
- NuGet and GHCR preview packages at v0.1-preview, after explicit approval.

No release date is promised. Progress is tracked through daily demos and weekly gates.

## Maintainers

Runesgalder is created and led by [@karlmoriaty](https://github.com/karlmoriaty), who owns the project and is the counterparty to the CLA. Contributors are credited in the repository history and in [AUTHORS](AUTHORS).

**The project is recruiting co-maintainers.** The goal is two to three active maintainers, so no single person is a bus factor. We are looking for people who can own at least one area for a few hours a week:

- Flow combinators, budgets, approvals, and journal semantics.
- Sandbox isolation, container hardening, and the escape corpus.
- MCP clients, integrations, and developer tooling.
- Documentation, quickstarts, and translations.
- CI, release engineering, and dependency hygiene.
- Security review of policy, generated code, and the execution boundary.

Maintenance is unpaid, and the [CLA](CLA.md) applies. If you are interested, open a [co-maintainer application](https://github.com/Runesgalder/Runesgalder/issues/new?template=maintainer_application.yml) and read [MAINTAINERS.md](MAINTAINERS.md) for the role, the path from contributor to maintainer, and what we ask for.

## Contributing

All code and documentation contributions require acceptance of the [CLA](CLA.md), version 1.0, effective 2026-11-23. [CLA Assistant](https://cla-assistant.io/Runesgalder/Runesgalder) records acceptance on your first pull request and reports it as a status check. Contributions made on behalf of a corporation require a signed [Corporate CLA](CORPORATE_CLA.md). Please open an issue before substantial work. Bug reports, design feedback, and reproducible test cases are welcome.

See [CONTRIBUTING.md](CONTRIBUTING.md) and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). The CLA is [CLA.md](CLA.md).

## Telemetry

Runesgalder has no phone-home behaviour. The intended default is local logs and local OpenTelemetry only. Remote telemetry, if ever added, must be opt-in and reviewed separately. The trace and metric contract and the host enablement snippet are in [docs/observability.md](docs/observability.md).

## Licence

MIT. See [LICENSE](LICENSE).

Copyright (c) 2026 karlmoriaty.
