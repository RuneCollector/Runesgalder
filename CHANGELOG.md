# Changelog

All notable changes to this project are recorded here. The format follows Keep a Changelog, and versions follow Semantic Versioning. No packages are published; release assets are built from the tagged source.

## [Unreleased]

### Added

- Project ownership and maintainer attribution in README, AUTHORS, and MAINTAINERS.md, with a public co-maintainer recruitment and application process.

## [0.1.0-preview.0] - 2026-11-25

First preview. Pre-alpha, single-tenant, Linux-container sandbox; not production ready.

### Added

- Typed Flow orchestration with `Step` composition, retry, cancellation, budgets, approvals, checkpoints, and a typed run-event journal.
- In-memory and SQLite journals and checkpoint stores, with recovery tests that assert no duplicate side effects.
- Flow traces and metrics on `Runesgalder.Flow` through BCL `ActivitySource` and `Meter`, with no SDK or exporter in the library.
- Sandbox policy contracts, Roslyn compilation with a namespace denylist and a reference whitelist, and a deterministic compiler.
- Hardened Linux worker image: no network, read-only root, `cap-drop ALL`, `no-new-privileges`, non-root user, and CPU, memory, PID, output, and wall-clock limits.
- MCP server over stdio with `compile_csharp`, `run_csharp`, `get_run`, and `cancel_run`; one run at a time and four queued.
- Sandbox and MCP traces and metrics on `Runesgalder.Sandbox`, covering compile and run spans, eight instruments, policy decisions, queue wait, output size, and resource kills.
- Escape corpus with eleven non-destructive cases, run by a Docker-gated test on the Linux CI lane.
- English, Chinese, and German quickstarts with Windows (WSL2/Docker) steps and a one-command sandbox image build.
- Release workflow for tag pushes: source-built archives, SHA-256 checksums, a dependency-graph SPDX SBOM, GitHub build-provenance attestations, and a GitHub Release.
- Two independent MCP client implementations in the test suite: the official client library and a hand-written JSON-RPC client.
- Project site at <https://runesgalder.github.io/>.

### Security

- Dependabot vulnerability alerts and automated security fixes are enabled; no open alerts at the time of release.
- The stdio surface accepts no host paths, URLs, environment values, secrets, or Docker options.
- Telemetry is off by default: no listener means no-op instrumentation, and the library ships no exporter or endpoint.

### Known limitations

- The Linux `sandboxd`, remote transport, and stronger isolation tiers are not implemented.
- Windows native isolation is deferred; use WSL2 or Docker.
- Cross-process trace context is not carried over stdio in v0.1.
- Peak CPU and memory usage are not measured; resource pressure is visible through limits, output size, and kills.
- Container tests skip on hosts without Docker and the worker image; the Linux CI lane is the evidence for the container path.
- End-user MCP clients (Claude Code, Cursor) were not exercised for this release; the stdio surface is covered by two independent client implementations in the test suite.