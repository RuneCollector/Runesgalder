# Observability

The Flow pillar emits OpenTelemetry-compatible traces and metrics through the base class library. The library only instruments; exporters and dashboards belong to the host. This page documents the contract from [ADR-0013](adr/0013-observability.md) and [ADR-0007](adr/0007-journal-and-recovery.md).

## Default behaviour

- Nothing leaves the machine. Runesgalder opens no socket and ships no exporter. Data moves only when a host registers a listener and adds an exporter of its own.
- With no listener registered, `ActivitySource.StartActivity` and every `Meter` call are no-ops. Instrumentation costs nothing when unobserved.
- The library never records flow inputs, step outputs, prompts, completions, or model payloads on spans, metrics, or instruments.
- Metrics carry no run, step, or approval identifiers. Those are trace and journal identifiers; putting them on metrics would create unbounded cardinality.

v0.1 covers Flow only. Sandbox, compilation, and the MCP server emit no telemetry yet; their attributes (`policy.decision`, `sandbox.backend`) and instruments arrive with their own ADRs.

## Source and meter

| Signal | Name | Version |
| --- | --- | --- |
| Tracing | `ActivitySource` `Runesgalder.Flow` | `0.1.0` |
| Metrics | `Meter` `Runesgalder.Flow` | `0.1.0` |

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

## Correlating signals

Trace, metric, and journal agree by construction:

- `flow.outcome` equals the error code on `RunCompleted` or `RunFailed`.
- A budget failure carries the same dimension, limit, and used values as `BudgetExceeded`.
- An approval denial carries the same identifier and decision as the approval journal records.

To answer a question about one run, start from its run span, then read the journal by `run.id`. To answer a question across runs, aggregate metrics by `flow.name`, `step.name`, and the outcome tags.

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
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddMeter("Runesgalder.Flow")
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

Useful first alerts: any `flow.budget.exceeded` above zero, a `flow.approval_denied` rate above the accepted baseline, and a p95 step duration above the flow's expected bound.

## Cardinality

Metrics aggregate by name, so keep step names low-cardinality. Do not build step names from user identifiers, request identifiers, or file names. When a per-request breakdown is needed, use the trace: the run span carries `run.id` and its children carry `step.id`.

## Payloads and privacy

The library records structure, not content. Host adapters that capture prompts, tool arguments, or completions do so under their own policy, must mask sensitive values, and should keep capture off by default. The no-payload promise is pinned by tests that fail if an input or output string appears in any span tag.

## Model child spans

A host adapter typically starts a span for each model call and sets the GenAI attributes from `FlowDiagnostics` (`ModelNameTag`, `TokenInputTag`, `TokenOutputTag`, `CostUsdTag`). The exact GenAI semantic-convention version is pinned by the host SDK; the Runesgalder names above do not change with it. Charge the same usage through `ChargeTokensAsync` and `ChargeUsdAsync` so the token and cost counters stay in step with the spans.

## Testing

The Flow test suite uses in-process `ActivityListener` and `MeterListener` instances and asserts the outcomes, governance attributes, and the absence of payloads. With no listener the same tests pass unchanged, which demonstrates the no-op default.

```bash
dotnet test tests/Runesgalder.Flow.Tests
```

## See also

- [ADR-0013: Metrics and governance attributes](adr/0013-observability.md)
- [ADR-0007: Journal, idempotency, and recovery semantics](adr/0007-journal-and-recovery.md)
- [ADR-0012: Budgets and approvals](adr/0012-budgets-and-approvals.md)