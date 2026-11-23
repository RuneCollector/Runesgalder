# ADR-0008: MCP surface for the sandbox

- Status: Proposed (awaiting acceptance)
- Date: 2026-11-23
- Deciders: karlmoriaty

## Context

MCP is the distribution path for people who work in Claude, Cursor, or another MCP client and want C# compilation and execution without building the sandbox themselves. The v0.1 product plan makes MCP a P0 and names four tools: `compile_csharp`, `run_csharp`, `get_run`, and `cancel_run`.

The MCP server is a client of the sandbox, not a second sandbox. It must not accept host paths, bypass the container policy, expose Docker control, or turn the server process into an execution surface. The v0.1 sandbox has a typed analysis contract (`IAnalysis`, `ITable`, `IScriptContext`) and no daemon yet, so the MCP surface has to be defined against the contract, not against a daemon protocol that does not exist.

## Decision

### Shape

- New project `Runesgalder.Mcp`; executable name `runesgalder-mcp`. It is not packable until the v0.1-preview packaging gate.
- Transport: stdio only for v0.1. No sockets, no HTTP listener, no authentication. stdout carries MCP frames only; logs go to stderr.
- SDK: `ModelContextProtocol` 2.4.0 (Apache-2.0, Anthropic and contributors) with `Microsoft.Extensions.*` 10.0.x (MIT). Version pinned; upgrades are tested and recorded like any dependency change.
- Tools only. No resources, prompts, sampling, or server-initiated requests in v0.1. The server never asks the client to run anything.
- Server name `runesgalder`; tool names and payload fields are snake_case.

### Contract

- v0.1 executes the analysis contract only: generated code must implement `Runesgalder.Sandbox.IAnalysis` with `RunAsync(ITable, IScriptContext)`. This is typed execution, not a general C# REPL.
- Input data is inline CSV. There are no file-path parameters. Omitting `data` means an empty table with no columns and no rows.
- The response returns structured results only: summary, charts, sandbox logs, diagnostics, resource figures. No raw stdout, no host paths, no environment values.
- General entry points for console-style code are deferred to a later ADR; adding one is a public API and policy change.

### Tools

`compile_csharp`

- Request: `code` (string, ≤ 256 KiB UTF-8), `contract` (optional, only `analysis` in v0.1).
- Response: `ok`, `diagnostics[]` (`id`, `severity`, `message`, `line`, `column`), `artifact` (`sha256`, `bytes`) or null, `durationMs`.
- Compiles on the host with the existing compiler; never executes code.

`run_csharp`

- Request: `code`, optional `data` (`name`, `csv`), optional `wait_seconds` (0–120, default 30), optional `wall_clock_seconds` (may only lower the server maximum).
- Response: an envelope with `run_id`, `status` (`queued`, `running`, `succeeded`, `failed`, `cancelled`, `timed_out`), and when terminal: `result` (`summary`, `charts`, `logs`), `diagnostics`, `resource` (`compile_ms`, `sandbox_ms`, `wall_ms`, `output_bytes`), `error` (`code`, `message`) or null.
- With `wait_seconds` greater than zero the call waits up to that long, then returns the current envelope. With zero it returns immediately after queueing.

`get_run`

- Request: `run_id`.
- Response: the same envelope. Unknown or evicted runs return `run_not_found`.

`cancel_run`

- Request: `run_id`.
- Response: `run_id` and `status`. Idempotent: cancelling a terminal run returns its terminal status unchanged.

### Runs and retention

- One run executes at a time per server process; up to four wait in a queue. A full queue returns `server_busy`.
- Run identifiers are opaque (`run_` prefix plus random); clients must not parse them.
- Runs live in memory for 30 minutes after a terminal state, capped at 100 runs; the oldest terminal run is evicted first. There is no cross-process recovery and no persistence of code, data, or results.
- Cancellation is cooperative first and terminates the container at the existing hard-deadline boundary.
- Compile and sandbox concurrency share the same queue so one client cannot occupy the host with parallel compilations.

### Limits

- Source: 256 KiB (existing `ScriptCompilerOptions.MaxSourceBytes`).
- Data: 8 MiB and 100,000 rows (existing worker limits).
- Memory: 256 MiB; output: 1 MiB policy cap with the worker's 64 KiB structured cap; CPU: 1 core; processes: 16.
- Wall clock: server default 30 s, operator maximum 120 s. Clients may request a lower value, never a higher one.
- Oversized input, unknown fields, or a contract name other than `analysis` are rejected before compilation.

### Security

- The server launches the existing hardened container: no network, read-only input, read-only root, tmpfs scratch, non-root, `cap-drop ALL`, `no-new-privileges`, memory/CPU/pid limits, hard deadline and kill.
- The MCP server needs Docker access as a host application. That access is never exposed as a tool, an argument, or a file path.
- No host paths, URLs, environment variables, secrets, or Docker options are accepted from clients.
- Errors are stable, structured codes: `invalid_request`, `source_too_large`, `data_too_large`, `contract_invalid`, `policy_denied`, `compile_failed`, `sandbox_unavailable`, `run_timeout`, `cancelled`, `server_busy`, `run_not_found`, `internal`.
- Tool failures that a user can act on are returned as normal tool results with an error code, not as protocol errors. Protocol errors are reserved for malformed MCP traffic.
- No telemetry, no phone-home, no prompt or completion storage. stderr logs carry run identifiers and timings only.

### Versioning and compatibility

- The server reports its own version and the supported contract (`analysis/v1`) at initialise.
- Tool schemas evolve additively in v0.1. Removing a field, changing a limit, or changing a status value requires an ADR update and acceptance, and a server version bump.
- Before the v0.1-preview gate, the server is smoke-tested in at least two clients; Claude Code and Cursor are the primary targets, with Claude Desktop if available.
- MCP protocol revisions are whatever the pinned SDK supports; upgrading the SDK is tested against the same client set.

### Testing

- Unit tests cover tool handlers with a fake runner: schema validation, limits, error mapping, queueing, cancellation, retention, and envelope shape.
- One integration test per platform CI lane compiles and runs a fixed script through the real container where Docker is available; otherwise the test is skipped with a recorded reason.
- Client smoke tests are manual, recorded in `docs/demos/`, and repeated at the v0.1-preview gate.

## Consequences

Positive:

- One MCP server gives any client typed C# execution with the same policy as the .NET API.
- Inline data and no path parameters remove the obvious host-file and traversal surface.
- The run envelope leaves room for budgets and approvals without changing tool names.

Negative:

- v0.1 cannot run arbitrary console-style C# through MCP; users must target the analysis contract.
- One run at a time limits throughput; a daemon with queueing remains future work.
- Results are lost when the server exits; durable runs need the daemon and journal integration.
- Waiting calls make long analyses dependent on client timeouts; polling with `get_run` is the reliable path.

## Alternatives considered

- **A single generic `run_csharp` with console semantics.** Rejected for v0.1: it needs a new entry contract, output capture, and a broader policy review.
- **Expose file paths for inputs and outputs.** Rejected: it invites traversal, symlink tricks, and host-file reads that inline CSV avoids.
- **HTTP/SSE transport first.** Rejected: stdio is the local default, needs no auth, and matches the v0.1 single-machine target. HTTP is planned for v0.5 with TLS and tokens.
- **One tool per step (compile, execute, fetch artifacts).** Rejected: it multiplies round trips and state without adding safety.
- **Embed Flow inside the MCP server.** Rejected: the MCP surface is a sandbox client; orchestration stays in Flow and in the client's agent.

## Dependency review

- Package: `ModelContextProtocol` 2.4.0.
  - Need: official MCP server implementation and protocol framing; reimplementing the protocol is not justified.
  - Licence: Apache-2.0 (Anthropic and contributors). Transitive: `ModelContextProtocol.Core` (Apache-2.0), `Microsoft.Extensions.Caching.Abstractions` and `Microsoft.Extensions.Hosting.Abstractions` 10.0.x (MIT).
  - Security note: stdio transport only in v0.1; no network listener; no telemetry; version pinned. SDK upgrades are re-reviewed against the client smoke set.