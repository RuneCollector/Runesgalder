# Runesgalder

A governed runtime for agent workflows and safe C# code execution.

Runesgalder is a pre-alpha .NET project with two first-class surfaces over one runtime:

- **Flow** — typed agent orchestration with LINQ-style composition, a typed event journal, retries, cancellation, and a stream of run events. A durable journal and OpenTelemetry export are planned.
- **Sandbox (in development)** — a deny-by-default execution boundary for untrusted, AI-generated C# code. Generated code is designed to receive host-defined contracts, not arbitrary files, network access, or package installation.

Both surfaces are designed around the same primitives: `Step`, `Effect`, `Policy`, `Journal`, `Artifact`, and `RunEvent`. The design goal is that every effect is governed.

## Status

**Pre-alpha. Not production ready. No packages have been published.**

| Area | State |
| --- | --- |
| Typed Flow steps, journal events, retry, cancellation | Working skeleton |
| Sandbox policy contracts and default-deny policy | Working skeleton |
| Roslyn compile pipeline, worker isolation, Linux `sandboxd` | Planned; not started |
| MCP server (`compile_csharp`, `run_csharp`, `get_run`, `cancel_run`) | Planned for v0.1 |
| English documentation | Available |
| Chinese and German Quickstarts | Planned |
| GitHub Actions CI (build, test, format) | Present |
| CodeQL, Dependabot, issue and pull-request templates | Present |
| Threat model and escape-corpus plan | Present; corpus not yet populated |

The v0.1 target is a Linux sandbox daemon with container isolation and a cross-platform client. Windows native strong isolation is deferred; development on Windows uses WSL2 or Docker.

## Quick start

Prerequisites:

- .NET SDK 10.0.401 or later in the 10.0 feature band
- Git

```bash
dotnet build Runesgalder.sln -c Release
dotnet test Runesgalder.sln -c Release
dotnet run --project src/Runesgalder.Cli -c Release -- demo
dotnet run --project src/Runesgalder.Cli -c Release -- doctor
```

`doctor` prints the runtime, platform, container detection, and whether the current host can run the Linux sandbox.

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

## Security warning

Do not run untrusted code with this repository yet. The sandbox implementation, Linux daemon, container hardening, and escape test corpus are not complete. The v0.1 isolation model is local, single-tenant, and container-based; it is not a claim of multi-tenant hardening.

Security reports should follow [SECURITY.md](SECURITY.md). The only security contact is `3070687583@qq.com`.

Runesgalder is independent and is not affiliated with or endorsed by Microsoft.

## Design documents

- [Project plan](docs/PROJECT_PLAN.md) — scope, v0.1 budget, delivery gates, and risks.
- [Architecture decisions](docs/adr/README.md) — the decisions behind the unified runtime, equality of the two surfaces, Linux-first delivery, isolation tiers, and Native AOT roles.
- [Threat model](security/threat-model.md) — assets, trust boundaries, threats, mitigations, and residual risk.
- [Demo log](docs/demos/) — daily evidence.
- [Acceptance log](docs/acceptance/) — what was accepted, by whom, and with what evidence.

## Repository layout

```text
.github/                          CI, CodeQL, Dependabot, issue and PR templates
src/
  Runesgalder.Abstractions/        Shared runtime primitives
  Runesgalder.Flow/                Typed flow orchestration
  Runesgalder.Sandbox.Abstractions/ Host contracts and sandbox policy
  Runesgalder.Cli/                 Development CLI
tests/
  Runesgalder.Flow.Tests/
  Runesgalder.Sandbox.Tests/
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

## Contributing

The project is preparing a Contributor Licence Agreement (CLA). Until the CLA is published and enforced, please open an issue before sending code. Documentation fixes, bug reports, and design feedback are welcome now.

See [CONTRIBUTING.md](CONTRIBUTING.md) and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). The CLA draft is [CLA.md](CLA.md) and is not yet in force.

## Telemetry

Runesgalder has no phone-home behaviour. The intended default is local logs and local OpenTelemetry only. Remote telemetry, if ever added, must be opt-in and reviewed separately.

## Licence

MIT. See [LICENSE](LICENSE).

Copyright (c) 2026 karlmoriaty.
