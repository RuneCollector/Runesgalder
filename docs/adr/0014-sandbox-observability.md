# ADR-0014: Sandbox and MCP observability for v0.1

- Status: Accepted
- Date: 2026-11-24
- Accepted: 2026-11-24
- Deciders: karlmoriaty

## Context

ADR-0013 defined the Flow trace and metric contract and deliberately left Sandbox, compilation, and MCP telemetry to their own records, including the `policy.decision` and `sandbox.backend` attributes. Today no assembly under `Runesgalder.Sandbox.*` or `Runesgalder.Mcp` creates an `ActivitySource` or `Meter`. Outcomes exist only as MCP error envelopes and `ContainerRunResult` fields, so compile failures, policy rejections, queueing, and resource kills cannot be counted.

The plan requires one trace per run, child spans for compilation and sandbox work, metrics for run counts, success rate, latency, compile failures, policy rejections, and sandbox resources, and structured logs that carry codes but never secrets or full data. It also requires that telemetry stay off unless a host opts in.

A 2026-11-24 gate review found that the container path is not exercised by CI: `security/escape-corpus/` contains no case files, and no workflow builds the worker image, so the Docker-gated tests skip everywhere. This ADR does not change that finding. It defines what the sandbox will emit once that path runs, so the missing coverage becomes visible rather than silent.

## Decision

### Instrumentation only, no SDK dependency

- The sandbox keeps using BCL types only: `ActivitySource` and `Meter` from `System.Diagnostics`. No `OpenTelemetry` package, no exporter, and no transitive OTel dependency.
- Hosts opt in by registering the `Runesgalder.Sandbox` source and meter with their own OTel SDK and exporter. Exporter wiring lives in host samples and documentation, never in the library.
- With no listener registered, `Activity` and `Meter` calls are no-ops, so telemetry remains off by default.
- The source and meter share the name `Runesgalder.Sandbox` and version `0.1.0`. Their names and the instrument and tag names below are a stable contract within 0.1.x; renames require a new ADR.

### Trace surface

Two spans cover the pillar:

| Span | Created by | Covers |
| --- | --- | --- |
| `compile csharp` | `Runesgalder.Sandbox.Compilation` | Policy scan, Roslyn parse, and emission |
| `sandbox run` | `Runesgalder.Mcp` run manager | Admission, queue wait, container execution, and terminal outcome |

The following attribute constants are added to a public `SandboxDiagnostics` class:

| Constant | Tag | Used on |
| --- | --- | --- |
| `RunIdTag` | `run.id` | Run span once a run is accepted; the same value the MCP envelope carries |
| `SandboxBackendTag` | `sandbox.backend` | Run span |
| `PolicyDecisionTag` | `policy.decision` | Compile and run spans |
| `PolicyReasonTag` | `policy.reason` | Compile and run spans when the decision is a denial |
| `CompileOutcomeTag` | `compile.outcome` | Compile span |
| `CompileDiagnosticsTag` | `compile.diagnostics` | Compile span; the number of error diagnostics |
| `SandboxOutcomeTag` | `sandbox.outcome` | Run span |
| `SandboxExitCodeTag` | `sandbox.exit_code` | Run span |
| `SandboxResultWrittenTag` | `sandbox.result_written` | Run span |
| `SandboxKillReasonTag` | `sandbox.kill_reason` | Run span when the container is killed |
| `SandboxLimitWallTag` | `sandbox.limit.wall_seconds` | Run span |
| `SandboxLimitCpuTag` | `sandbox.limit.cpu_cores` | Run span |
| `SandboxLimitMemoryTag` | `sandbox.limit.memory_bytes` | Run span |
| `SandboxLimitPidsTag` | `sandbox.limit.pids` | Run span |
| `SandboxLimitOutputTag` | `sandbox.limit.output_bytes` | Run span |
| `SandboxUsedWallTag` | `sandbox.used.wall_seconds` | Run span |
| `SandboxUsedOutputTag` | `sandbox.used.output_bytes` | Run span |

- `sandbox.backend` is `container` in v0.1. Values such as `gvisor` and `microvm` are reserved for the isolation-tier work and are not emitted until a backend exists.
- `policy.decision` is `allow` or `deny`. A denial also sets `policy.reason` to a fixed value: `policy_denied` for the banned-namespace and runtime-policy checks, or `source_too_large` and `data_too_large` for admission bounds. Contract and compile errors are not policy denials and carry only `compile.outcome`.
- `compile.outcome` is `ok`, `contract_invalid`, `policy_denied`, `compile_failed`, or `source_too_large`, matching the MCP compile error codes.
- `sandbox.outcome` uses the same vocabulary as the MCP error envelope for terminal execution states: `ok`, `contract_invalid`, `policy_denied`, `compile_failed`, `source_too_large`, `data_too_large`, `run_timeout`, `cancelled`, `sandbox_unavailable`, or `internal`. The worker's `output-quota` continues to map to `policy_denied`, as it does today.
- `sandbox.kill_reason` is `wall`, `cancelled`, or `crash`. It is set only when the container is killed rather than exiting on its own.
- Limit attributes are the effective values from `SandboxPolicy` after MCP defaults are applied, not the requested values. Used attributes are measured: wall seconds from the run stopwatch and output bytes from the result file. Peak CPU and memory usage are not measured in v0.1 and therefore do not appear as used attributes.
- A denied `run_csharp` attempt still produces a short `sandbox run` span with `policy.decision=deny` and no `run.id`, so a rejection is visible in the trace as well as the counter.

The library never records source code, inline CSV, result JSON, chart values, container stdout or stderr, environment values, image pull credentials, or host paths on spans or metrics. Compile diagnostic messages stay in the MCP response and are not copied to spans.

### Metrics

A single `Meter` named `Runesgalder.Sandbox` exposes these instruments:

| Instrument | Kind | Unit | Tags | Recorded when |
| --- | --- | --- | --- | --- |
| `sandbox.runs` | Counter\<long\> | `{run}` | `sandbox.backend`, `sandbox.outcome` | An accepted run reaches a terminal state |
| `sandbox.run.duration` | Histogram\<double\> | `s` | `sandbox.backend`, `sandbox.outcome` | An accepted run reaches a terminal state |
| `sandbox.queue.wait` | Histogram\<double\> | `s` | `sandbox.backend` | An accepted run leaves the queue |
| `sandbox.compile.runs` | Counter\<long\> | `{compilation}` | `compile.outcome` | A compilation ends |
| `sandbox.compile.duration` | Histogram\<double\> | `s` | `compile.outcome` | A compilation ends |
| `sandbox.policy.decisions` | Counter\<long\> | `{decision}` | `policy.decision`, `policy.reason` | A security-policy decision is made |
| `sandbox.output.bytes` | Histogram\<long\> | `By` | `sandbox.backend`, `sandbox.outcome` | A run ends with a written result |
| `sandbox.kills` | Counter\<long\> | `{kill}` | `sandbox.kill_reason` | The runner kills a container |

- `sandbox.runs` and `sandbox.run.duration` count accepted runs only. Submissions rejected for capacity (`server_busy`) or malformed input (`invalid_request`) are client errors and are not counted as runs.
- `sandbox.policy.decisions` counts compile-time namespace denials and admission denials for source or data size. It does not count contract errors, compile errors, capacity, or malformed requests. `policy.reason` is present only on denials.
- `sandbox.queue.wait` measures acceptance to container start, so the one-running plus four-queued limit from ADR-0008 becomes observable.
- `sandbox.output.bytes` records the result size, not the inline CSV size or any chart payload. It is a histogram so a p99 view is available without per-run tags.
- Metrics deliberately carry no `run.id`, container id, file name, path, or hash. Correlate a metric to a specific run through that run's span and the MCP envelope.
- Compile metrics are recorded by the compilation assembly, so library users who compile without MCP still get them. Run metrics are recorded by the MCP run manager. No instrument is created or recorded twice.

### Correlation across surfaces

- `run.id` is the join key between the MCP envelope, the run span, and host logs. A compilation that precedes a run has no `run.id`.
- When a Flow host calls the sandbox in process, `Activity.Current` propagates naturally. Over the stdio MCP boundary, v0.1 does not carry W3C trace context, because the negotiated MCP envelope has no agreed trace field and inventing one would change the surface ADR-0008 fixed. Cross-process single-trace correlation is deferred to the remote daemon work in v0.5.
- Until then, operators correlate by `run.id` and by the `sandbox.*` metric dimensions; this limitation is documented rather than hidden.

### Logging

- MCP server logs stay on stderr with stdout reserved for protocol frames. Sandbox and MCP log entries carry `run.id`, `sandbox.outcome` or `compile.outcome`, and the error code.
- Logs never include source code, CSV rows, result JSON, chart values, container stdout or stderr, environment values, or absolute host paths. The bounded `StandardError` field from `ContainerRunResult` is diagnostic input only and is not logged.
- The journal remains the Flow audit trail; the sandbox adds no second audit store in v0.1.

### Plumbing

- `SandboxDiagnostics` lives in `Runesgalder.Sandbox.Abstractions` so compilation, containers, and MCP share one source and one tag vocabulary.
- `SandboxMetrics` is internal to `Runesgalder.Sandbox.Abstractions`; the compilation, container, and MCP assemblies and the test projects gain `InternalsVisibleTo` access. The public surface grows only by the tag constants and the source.
- The MCP run record already carries creation, start, and terminal timestamps through `TimeProvider`; queue wait and duration reuse them rather than adding timers.
- The run manager records at the same points that already build the terminal envelope, so a span and its metrics agree with what the client receives.

### Testing

- In-process `ActivityListener` tests cover compile success, compile policy denial, and contract failure without Docker. Run-span tests use a fake container runner so success, timeout, cancellation, admission denial, and kill reasons are asserted on every lane.
- In-process `MeterListener` tests cover all eight instruments, including queue wait, output size, and kill reason.
- Tests assert that no span or metric tag carries source, CSV, result, chart, container stderr, or environment text.
- One Docker-gated end-to-end test asserts the real exit code, result flag, and output bytes; it is expected to skip locally and is not claimed as evidence until CI builds the image.
- With no listener registered, the suite still passes unchanged, demonstrating the no-op default.

## Consequences

Positive:

- Compile failure rate and policy rejection rate become countable, which is the first half of the plan's observability requirement.
- Queue saturation and resource kills become visible without reading logs.
- Traces, metrics, MCP envelopes, and logs share one outcome vocabulary, so an operator can move between them without translation.
- Library users who compile or run without MCP still receive traces and compile metrics.

Negative:

- Instrument and tag names become a compatibility surface.
- Peak CPU and memory usage remain unmeasured in v0.1, so resource pressure is visible only through limits, output size, and kills.
- Cross-process tracing over stdio is not available in v0.1 and must be explained in the documentation.
- The `InternalsVisibleTo` plumbing couples the sandbox assemblies more tightly than the Flow equivalent.
- Metrics become useful only when the container path actually runs; the CI gap identified at the gate is still open.

## Alternatives considered

- **Reference the OpenTelemetry SDK or an exporter in the library.** Rejected for the same reasons as ADR-0013: dependency, version churn, and telemetry configuration inside a runtime library.
- **Instrument only in MCP.** Rejected: compilation and direct container users would emit nothing, and the plan treats the sandbox as a first-class surface.
- **Add a trace-context field to the stdio envelope.** Rejected for v0.1: it changes the MCP surface fixed by ADR-0008 and needs client cooperation. `run.id` correlation is sufficient until the daemon arrives.
- **Tag spans or metrics with run, container, or file identifiers.** Rejected: high cardinality on metrics and unnecessary path disclosure on spans.
- **Emit peak memory and CPU gauges now.** Deferred: the container runner does not read cgroup statistics, and guessing or sampling would produce misleading data.
- **Count every rejected request as a run.** Rejected: capacity and malformed-request errors are client outcomes, not sandbox runs, and would skew duration histograms.

## Verification plan

- In-process `ActivityListener` and `MeterListener` tests in `Runesgalder.Sandbox.Tests` and `Runesgalder.Mcp.Tests`, with no new package references.
- The full suite and `dotnet format --verify-no-changes` stay clean.
- A demo records compile and run span attributes and the metric transcript, including a denial and a queue wait.
- Both CI lanes pass for the implementing commit. A Docker lane is still required before any resource claim is treated as verified.

## Dependency review

- No new package. The change uses `System.Diagnostics` and `System.Diagnostics.Metrics`, both in the shared framework.
- No new secret, network, filesystem, or process surface. Instrumentation is in-process only and records no payloads.