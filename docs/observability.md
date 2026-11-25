# Observability

The Flow and Sandbox pillars emit OpenTelemetry-compatible traces and metrics through the base class library. The library only instruments; exporters and dashboards belong to the host. This page documents the contract from [ADR-0013](adr/0013-observability.md), [ADR-0014](adr/0014-sandbox-observability.md), and [ADR-0007](adr/0007-journal-and-recovery.md).

## Default behaviour

- Nothing leaves the machine. Runesgalder opens no socket and ships no exporter. Data moves only when a host registers a listener and adds an exporter of its own.
- With no listener registered, `ActivitySource.StartActivity` and every `Meter` call are no-ops. Instrumentation costs nothing when unobserved.
- The library never records flow inputs, step outputs, prompts, completions, or model payloads on spans, metrics, or instruments.
- Metrics carry no run, step, or approval identifiers. Those are trace and journal identifiers; putting them on metrics would create unbounded cardinality.

v0.1 covers Flow, sandbox compilation, and the MCP sandbox runner. Both pillars follow the same pattern: BCL instrumentation only, no SDK or exporter, and nothing is emitted unless a host registers the source and meter.

## Source and meter

| Signal | Name | Version |
| --- | --- | --- |
| Tracing | `ActivitySource` `Runesgalder.Flow` | `0.1.0` |
| Metrics | `Meter` `Runesgalder.Flow` | `0.1.0` |
| Tracing | `ActivitySource` `Runesgalder.Sandbox` | `0.1.0` |
| Metrics | `Meter` `Runesgalder.Sandbox` | `0.1.0` |

The names and the instrument and attribute names below are a stable contract within 0.1.x. Renaming requires a new ADR.

## Traces

| Span | Created when | Attributes |
| --- | --- | --- |
| `flow <name>` | A run starts; ends with the run | `run.id`, `flow.name`, `flow.outcome`; on a budget failure also `budget.dimension`, `budget.limit`, `budget.used`; on a denial or timeout also `approval.id`, `approval.decision` |
| `<step name>` | A step runs | `run.id`, `step.id`, `step.name`; on a budget failure also `budget.dimension`, `budget.limit`, `budget.used` |
| Model child span | A host model adapter calls a provider | `model.name`, `token.input`, `token.output`, and optionally `cost.usd` |

Step spans nest under the run span because steps start while the run activity is current. Failures set the span status to error with the exception message.

`flow.outcome` uses the same stable codes as the journal:

| Value | Meaning |
| --- | --- |
| `ok` | The run completed |
| `flow.cancelled` | The caller cancelled |
| `flow.timed_out` | A `TimeoutException` ended the run |
| `flow.budget_exceeded` | A hard budget limit tripped |
| `flow.approval_denied` | An approval was denied or timed out |
| `flow.step_failed` | Any other exception surfaced from the pipeline |

Attribute notes:

- Budget limits and usage are recorded as doubles. Wall values are seconds. The journal keeps the original decimals.
- The approval wrapper runs outside any step, so an approval denial decorates the run span only.
- `cost.usd` is host-supplied. The runtime never infers a provider's price table.
- The existing ADR-0007 tags keep their meaning: `run.id` is the run identity, `step.id` is the step identity, and `flow.name` and `step.name` are the human-readable names used for joins.

## Metrics

| Instrument | Kind | Unit | Tags | Recorded when |
| --- | --- | --- | --- | --- |
| `flow.runs` | Counter\<long\> | `{run}` | `flow.name`, `flow.outcome` | A run ends |
| `flow.steps` | Counter\<long\> | `{step}` | `flow.name`, `step.name`, `step.outcome` | A step ends |
| `flow.step.duration` | Histogram\<double\> | `s` | `flow.name`, `step.name`, `step.outcome` | A step ends |
| `flow.tokens` | Counter\<long\> | `{token}` | `flow.name` | A charge reaches `ChargeTokensAsync` |
| `flow.cost.usd` | Counter\<double\> | `USD` | `flow.name` | A charge reaches `ChargeUsdAsync` |
| `flow.budget.warnings` | Counter\<long\> | `{warning}` | `flow.name`, `budget.dimension` | A warn threshold is crossed |
| `flow.budget.exceeded` | Counter\<long\> | `{exceeded}` | `flow.name`, `budget.dimension` | A hard limit trips |
| `flow.approvals` | Counter\<long\> | `{decision}` | `flow.name`, `approval.decision` | An approval resolves |

- `step.outcome` is `ok` or the same stable error codes as `flow.outcome`.
- `budget.dimension` is `Tokens`, `Usd`, or `Wall`. `approval.decision` is `Approved`, `Denied`, or `TimedOut`.
- Token and USD counters record every charge, with or without an attached budget. Warning and exceeded counters record only from active budget scopes.
- `flow.approvals` counts the decision actually reached. Caller cancellation writes no decision and increments nothing.
- The token and cost counters are only as accurate as the host adapters that charge usage. Charge through `FlowContext.ChargeTokensAsync` and `ChargeUsdAsync`; nothing is measured by the runtime on its own.

## Sandbox and MCP traces

| Span | Created when | Attributes |
| --- | --- | --- |
| `compile csharp` | A script is compiled, whether through the library or the MCP `compile_csharp` tool | `compile.outcome`, `compile.diagnostics`; `policy.decision`, and `policy.reason` on a denial |
| `sandbox run` | The MCP run manager accepts a run | `run.id`, `sandbox.backend`, limits, `policy.decision`; on completion also `sandbox.outcome`, `sandbox.exit_code`, `sandbox.result_written`, `sandbox.used.wall_seconds`, `sandbox.used.output_bytes`, and `sandbox.kill_reason` when the container was killed |

`sandbox.backend` is `container` in v0.1. Limit attributes are the effective values after MCP defaults are applied: `sandbox.limit.wall_seconds`, `sandbox.limit.cpu_cores`, `sandbox.limit.memory_bytes`, `sandbox.limit.pids`, `sandbox.limit.output_bytes`.

`compile.outcome` is `ok`, `contract_invalid`, `policy_denied`, `compile_failed`, or `source_too_large`. `sandbox.outcome` uses the MCP error vocabulary: `ok`, `contract_invalid`, `policy_denied`, `compile_failed`, `source_too_large`, `data_too_large`, `run_timeout`, `cancelled`, `sandbox_unavailable`, or `internal`. The worker's `output-quota` failure maps to `policy_denied`, as it does in the envelope.

`policy.decision` is `allow` or `deny`. A denial sets `policy.reason` to `policy_denied`, `source_too_large`, or `data_too_large`. Contract and compile errors are not policy denials. `sandbox.kill_reason` is `wall` or `cancelled` and appears only when the runner stopped the container.

An admitted run that is later rejected for input size still records a `sandbox run` span without `run.id`, so a refusal is visible in the trace and in `sandbox.policy.decisions`. The compile span is created by the compilation assembly, so hosts that compile without MCP still receive it.

## Sandbox and MCP metrics

| Instrument | Kind | Unit | Tags | Recorded when |
| --- | --- | --- | --- | --- |
| `sandbox.runs` | Counter\<long\> | `{run}` | `sandbox.backend`, `sandbox.outcome` | An accepted run reaches a terminal state |
| `sandbox.run.duration` | Histogram\<double\> | `s` | `sandbox.backend`, `sandbox.outcome` | An accepted run reaches a terminal state |
| `sandbox.queue.wait` | Histogram\<double\> | `s` | `sandbox.backend` | An accepted run leaves the queue for the container |
| `sandbox.compile.runs` | Counter\<long\> | `{compilation}` | `compile.outcome` | A compilation ends |
| `sandbox.compile.duration` | Histogram\<double\> | `s` | `compile.outcome` | A compilation ends |
| `sandbox.policy.decisions` | Counter\<long\> | `{decision}` | `policy.decision`, `policy.reason` on denials | A security-policy decision is made |
| `sandbox.output.bytes` | Histogram\<long\> | `By` | `sandbox.backend`, `sandbox.outcome` | A run ends with a written result |
| `sandbox.kills` | Counter\<long\> | `{kill}` | `sandbox.kill_reason` | The runner kills a container |

- Only accepted runs count towards `sandbox.runs`, `sandbox.run.duration`, and `sandbox.output.bytes`. Capacity rejections (`server_busy`) and malformed requests (`invalid_request`) are client errors and are not runs.
- `sandbox.policy.decisions` counts compile-time namespace denials and admission denials for source or data size. Contract errors, compile errors, capacity, and malformed requests are excluded.
- `sandbox.queue.wait` measures acceptance to container start, so the queue behind the concurrency limit is observable.
- `sandbox.output.bytes` is the result file size, not the inline data size, and is a histogram so a p99 view needs no per-run tag.
- Metrics carry no run id, container id, file name, path, or hash. Correlate a metric to a run through that run's span and the MCP envelope.
- Compile metrics come from the compilation assembly and run metrics from the MCP run manager, so no instrument is created or recorded twice.

## Correlating signals

Trace, metric, and journal agree by construction:

- `flow.outcome` equals the error code on `RunCompleted` or `RunFailed`.
- A budget failure carries the same dimension, limit, and used values as `BudgetExceeded`.
- An approval denial carries the same identifier and decision as the approval journal records.
- `sandbox.outcome` equals the MCP error code in the terminal envelope, and `run.id` is the same value the envelope carries. Used wall seconds match the resource report.

To answer a question about one run, start from its run span, then read the journal by `run.id`. To answer a question across runs, aggregate metrics by `flow.name`, `step.name`, and the outcome tags.

Cross-process tracing is a v0.1 limitation. When a Flow host calls the sandbox in process, `Activity.Current` propagates and the sandbox spans nest under the flow span. Over the stdio MCP boundary, W3C trace context is not carried, because the negotiated envelope has no agreed trace field. A remote daemon is expected to close that gap; until then, correlate by `run.id` and the `sandbox.*` metric dimensions.

## Enabling in a host

No library configuration is required. Register the source and meter with whatever exporter the host already runs.

With the OpenTelemetry SDK (host packages, not Runesgalder dependencies):

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.Console
```

```csharp
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Runesgalder.Flow")
        .AddSource("Runesgalder.Sandbox")
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddMeter("Runesgalder.Flow")
        .AddMeter("Runesgalder.Sandbox")
        .AddConsoleExporter());
```

Outside an ASP.NET Core host, build the providers with `Sdk.CreateTracerProviderBuilder()` and `Sdk.CreateMeterProviderBuilder()` and dispose them when the application exits.

The console exporter writes to stdout and keeps data local. To send data to a collector, add the OTLP exporter and point it at an endpoint you control, for example a collector on `http://localhost:4317`. The exporter endpoint is the only switch that can move telemetry off the machine; Runesgalder sets no endpoint and no exporter. Keep endpoints and exporter credentials in host configuration, never in this repository.

## What to watch

| Question | Signal |
| --- | --- |
| Run success rate | `flow.runs` grouped by `flow.name` and `flow.outcome` |
| Step latency | `flow.step.duration` p95 and p99 by `flow.name` and `step.name` |
| Token spend per flow | `flow.tokens` by `flow.name` |
| Spend in currency | `flow.cost.usd` by `flow.name` |
| Budget pressure | `flow.budget.warnings` and `flow.budget.exceeded` by `budget.dimension` |
| Human gates | `flow.approvals` by `approval.decision` |
| Sandbox success rate | `sandbox.runs` grouped by `sandbox.outcome` |
| Sandbox latency | `sandbox.run.duration` p95 and p99 by `sandbox.outcome` |
| Compile failures | `sandbox.compile.runs` by `compile.outcome` |
| Policy pressure | `sandbox.policy.decisions` by `policy.decision` and `policy.reason` |
| Queue saturation | `sandbox.queue.wait` p95 |
| Result size | `sandbox.output.bytes` p95 and p99 |
| Resource kills | `sandbox.kills` by `sandbox.kill_reason` |

Useful first alerts: any `flow.budget.exceeded` above zero, a `flow.approval_denied` rate above the accepted baseline, and a p95 step duration above the flow's expected bound. For the sandbox, alert on any `sandbox.kills`, a rising `sandbox.compile.runs{compile.outcome="policy_denied"}`, and a `sandbox.queue.wait` p95 that approaches the MCP queue's tolerance.

## Cardinality

Metrics aggregate by name, so keep step names low-cardinality. Do not build step names from user identifiers, request identifiers, or file names. When a per-request breakdown is needed, use the trace: the run span carries `run.id` and its children carry `step.id`.

The sandbox instruments follow the same rule. They carry no run, container, file, or path dimension. Use `sandbox.outcome`, `compile.outcome`, `policy.decision`, `policy.reason`, and `sandbox.kill_reason`; these are fixed vocabularies, not values from the request.

## Payloads and privacy

The library records structure, not content. Host adapters that capture prompts, tool arguments, or completions do so under their own policy, must mask sensitive values, and should keep capture off by default. The no-payload promise is pinned by tests that fail if an input or output string appears in any span tag.

The sandbox adds the same rule: no source code, inline data, result JSON, chart values, container output, environment values, or host paths appear on spans or metrics. Bounded container stderr stays a diagnostic field in `ContainerRunResult` and is not copied to telemetry.

## Model child spans

A host adapter typically starts a span for each model call and sets the GenAI attributes from `FlowDiagnostics` (`ModelNameTag`, `TokenInputTag`, `TokenOutputTag`, `CostUsdTag`). The exact GenAI semantic-convention version is pinned by the host SDK; the Runesgalder names above do not change with it. Charge the same usage through `ChargeTokensAsync` and `ChargeUsdAsync` so the token and cost counters stay in step with the spans.

## Testing

The Flow test suite uses in-process `ActivityListener` and `MeterListener` instances and asserts the outcomes, governance attributes, and the absence of payloads. The sandbox suite does the same for compile and run spans, the eight instruments, deny decisions, kill reasons, and output size. With no listener the same tests pass unchanged, which demonstrates the no-op default.

```bash
dotnet test tests/Runesgalder.Flow.Tests
dotnet test tests/Runesgalder.Sandbox.Tests --filter FullyQualifiedName~SandboxDiagnosticsTests
dotnet test tests/Runesgalder.Mcp.Tests --filter FullyQualifiedName~RunDiagnosticsTests
```

The end-to-end run assertions need Docker and the worker image; elsewhere they are skipped. The escape corpus in `security/escape-corpus/` is exercised by its own Docker-gated test and is not claimed as evidence until the Linux CI lane builds the image.

## See also

- [ADR-0013: Metrics and governance attributes](adr/0013-observability.md)
- [ADR-0014: Sandbox and MCP observability](adr/0014-sandbox-observability.md)
- [ADR-0007: Journal, idempotency, and recovery semantics](adr/0007-journal-and-recovery.md)
- [ADR-0012: Budgets and approvals](adr/0012-budgets-and-approvals.md)