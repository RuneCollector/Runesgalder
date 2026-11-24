# ADR-0013: Metrics and governance attributes for v0.1 observability

- Status: Accepted
- Date: 2026-11-24
- Accepted: 2026-11-24
- Deciders: karlmoriaty

## Context

Phase 2 requires OTel observability as a P0 Flow item: one trace per run, child spans for steps, model calls, tools, compilation, and sandbox work, and metrics for runs, success rate, latency, tokens, cost, compile failures, policy rejections, and sandbox resources. Telemetry must stay fully off by default: local logs and local OTel only, no phone-home, and prompt or completion capture off by default.

Today the library emits a BCL `ActivitySource` named `Runesgalder.Flow` (ADR-0007) with run and step spans and the tags `run.id`, `step.id`, `flow.name`, `model.name`, `token.input`, and `token.output`. There is no metric surface, and the governance features added in ADR-0012 (budgets, approvals) are visible only through the journal and span status, not through attributes or counters.

The plan also calls for GenAI semantic conventions and a documented attribute mapping. Adding a `Meter` and new tag constants is a public API change, and any exporter would add a dependency, so both need a decision before implementation. The Sandbox and MCP pillars do not yet emit their own telemetry.

## Decision

### Instrumentation only, no SDK dependency

- The library keeps using BCL types only: `ActivitySource` and `Meter` from `System.Diagnostics`. No `OpenTelemetry`, no exporter package, and no transitive OTel dependency.
- Hosts opt in by registering the `Runesgalder.Flow` source and meter with their own OTel SDK and exporter. Exporter wiring lives in host samples and documentation, never in the library.
- With no listener registered, `Activity` and `Meter` calls are no-ops, so telemetry remains off by default and costs nothing when unobserved.
- The source and meter share the name `Runesgalder.Flow` and version `0.1.0`. Their names and the instrument names below are a stable contract within 0.1.x; renames require a new ADR.

### Trace surface

Existing span names and the tags from ADR-0007 stay unchanged. The following attribute constants are added to `FlowDiagnostics`:

| Constant | Tag | Used on |
| --- | --- | --- |
| `RunOutcomeTag` | `flow.outcome` | Run span |
| `StepNameTag` | `step.name` | Step span |
| `BudgetDimensionTag` | `budget.dimension` | Step and run spans when a budget fails |
| `BudgetLimitTag` | `budget.limit` | Step and run spans when a budget fails |
| `BudgetUsedTag` | `budget.used` | Step and run spans when a budget fails |
| `ApprovalIdTag` | `approval.id` | Run span when an approval is denied or times out |
| `ApprovalDecisionTag` | `approval.decision` | Run span when an approval is denied or times out |
| `CostUsdTag` | `cost.usd` | Step spans; set by host model adapters |

- `flow.outcome` is `ok` on success, otherwise the stable error code of the terminal event (`flow.cancelled`, `flow.timed_out`, `flow.budget_exceeded`, `flow.approval_denied`, `flow.step_failed`). The value is the same one the journal records, so traces and journals agree.
- Step spans gain `step.name` so metric and span views can be joined by the human-readable name; `step.id` remains the identity.
- A budget failure sets `budget.dimension`, `budget.limit`, and `budget.used` on the failing step span and the run span, taken from `BudgetExceededException`. Wall values are in seconds, matching the journal payload.
- An approval denial or timeout sets `approval.id` and `approval.decision` on the run span. The approval wrapper runs outside any step, so there is no step span to decorate.
- `cost.usd` is a documented attribute for model adapter child spans. The runtime never infers cost; only host adapters know a provider's price table.
- The library never records flow inputs, step outputs, prompts, or completions on spans or metrics. Host adapters that capture prompts must mask them and do so under their own policy.

### Metrics

A single `Meter` named `Runesgalder.Flow` exposes these instruments:

| Instrument | Kind | Unit | Tags | Recorded when |
| --- | --- | --- | --- | --- |
| `flow.runs` | Counter\<long\> | `{run}` | `flow.name`, `flow.outcome` | A run ends |
| `flow.steps` | Counter\<long\> | `{step}` | `flow.name`, `step.name`, `step.outcome` | A step ends |
| `flow.step.duration` | Histogram\<double\> | `s` | `flow.name`, `step.name`, `step.outcome` | A step ends |
| `flow.tokens` | Counter\<long\> | `{token}` | `flow.name` | A charge reaches `ChargeTokensAsync` |
| `flow.cost.usd` | Counter\<double\> | `USD` | `flow.name` | A charge reaches `ChargeUsdAsync` |
| `flow.budget.warnings` | Counter\<long\> | `{warning}` | `flow.name`, `budget.dimension` | A budget warning fires |
| `flow.budget.exceeded` | Counter\<long\> | `{exceeded}` | `flow.name`, `budget.dimension` | A hard budget limit trips |
| `flow.approvals` | Counter\<long\> | `{decision}` | `flow.name`, `approval.decision` | An approval resolves |

- `step.outcome` is `ok` or the stable error code, mirroring `flow.outcome`. `approval.decision` uses the `ApprovalDecision` names (`Approved`, `Denied`, `TimedOut`). `budget.dimension` uses the `BudgetDimension` names (`Tokens`, `Usd`, `Wall`).
- Token and cost counters record every charge, whether or not a budget scope is active. Warning and exceeded counters record only from budget scopes, so a charge without a budget produces no governance metrics.
- `flow.approvals` counts the decision actually reached. Caller cancellation writes no decision and therefore increments nothing, matching the journal rule from ADR-0012.
- Metrics deliberately carry no `run.id`, `step.id`, or `approval.id`: those are trace and journal identifiers, and high-cardinality metric tags are a cost trap. Correlate metrics to a specific run through that run's span.
- Durations are measured with `Stopwatch.GetElapsedTime` where a stopwatch is already taken. No new per-step allocation beyond the instrument call is required.

### Plumbing

- A new internal `FlowMetrics` static class owns the `Meter` and instruments and exposes small record methods. It is internal so the public surface grows only by the tag constants.
- `FlowContext` gains an internal `FlowName` property, assigned once in `RunCoreAsync`, so charging and approval code can tag its own metrics without threading the name through every signature. The public constructor and members are unchanged.
- Run and step completion points call `FlowMetrics` next to the existing journal append. Budget and approval record from `BudgetScope` and `RequireApproval`, where the dimension and decision are known.

### Semantic conventions and documentation

- v0.1 uses the GenAI conventions where a host emits model child spans (`model.name`, token counts, `cost.usd`). The exact convention version is pinned by the host SDK; the Runesgalder tag and instrument names above are the library's own contract and do not change with that version.
- `docs/observability.md` will document the mapping, the enablement snippet for a host, and the promise that nothing leaves the machine by default. It lands before the v0.1-preview gate.
- Sandbox, compile, and MCP metrics and the `policy.decision` and `sandbox.backend` attributes belong to those pillars and their own ADRs. This ADR covers Flow plus the shared governance tags it already owns.

### Testing

- In-process `ActivityListener` tests extend the existing diagnostic tests: a successful run tags `flow.outcome=ok`; cancellation and budget failure carry the stable code; a budget failure carries `budget.dimension`, `budget.limit`, and `budget.used`; a denial carries `approval.id` and `approval.decision`.
- In-process `MeterListener` tests cover every instrument: run and step counters with outcome tags, the duration histogram, token and cost counters on charge, warning and exceeded counters per dimension, and one approval decision count per outcome.
- Tests assert that no span or metric tag carries input, output, prompt, or completion text.
- With no listener registered, the suite still passes unchanged, demonstrating the no-op default.

## Consequences

Positive:

- The Flow pillar meets the Phase 2 metrics requirement without adding a dependency, a licence, or a supply-chain surface.
- Traces, metrics, and journals now name the same outcomes and dimensions, so an operator can move between them without translating.
- Governance failures become countable and alertable rather than only readable in a journal.
- Telemetry stays off unless a host opts in, preserving the stated privacy default.

Negative:

- Metric names and tags become a compatibility surface; renaming them later is a breaking change.
- Hosts must do their own exporter wiring; there is no turnkey console output in the library.
- Token and USD counters are only as accurate as host adapters' charging, the same limitation the budget feature already carries.
- High-cardinality tags are excluded by decision, so some per-run questions are answered from traces rather than metrics.
- Sandbox and MCP remain uninstrumented until their own records land.

## Alternatives considered

- **Reference the OpenTelemetry SDK or an exporter in the library.** Rejected: it adds a package, version churn, and telemetry configuration baked into a runtime library, and it conflicts with the off-by-default promise. Hosts already own their OTel setup.
- **Ship a console exporter behind a flag.** Rejected for v0.1: it is still a dependency for every user, and a sample host demonstrates it with no cost to the library.
- **Spans only, no `Meter`.** Rejected: the plan requires token and cost metrics, and metrics are the right shape for rates and alerts.
- **Tag spans with `cost.usd` computed by the runtime.** Rejected: the runtime has no price table and would guess.
- **Include `run.id` on metrics for per-run queries.** Rejected: unbounded cardinality; the trace carries the run identity.
- **Write structured logs from the runtime.** Deferred: the journal is the audit trail, and logging libraries belong to the host.

## Verification plan

- Unit tests in `Runesgalder.Flow.Tests` using `ActivityListener` and `MeterListener`, with no new package references.
- The full suite and `dotnet format --verify-no-changes` stay clean.
- A demo records the metrics transcript and the governance span attributes.
- Both CI lanes pass for the implementing commit.

## Dependency review

- No new package. The change uses `System.Diagnostics.Metrics`, part of the shared framework.
- No new secret, network, filesystem, or process surface. Instrumentation is in-process only.

## Implementation evidence (same day)

- `FlowDiagnostics` gained eight public tag constants: `flow.outcome`, `step.name`, `budget.dimension`, `budget.limit`, `budget.used`, `approval.id`, `approval.decision`, `cost.usd`.
- Internal `FlowMetrics` owns the `Runesgalder.Flow` meter and the eight instruments; run and step completion, charging, budget scopes, wall warnings, and approval decisions record through it.
- `FlowContext` gained an internal `FlowName` so charging and approval code can tag metrics; the public surface is unchanged.
- Run and step spans set `flow.outcome` and `step.name`; budget failures set dimension, limit, and used on both spans; approval denials set the identifier and decision on the run span.
- Flow tests: 94 passed (previously 85), including five `MeterListener` tests and four `ActivityListener` tests.
- Solution build and `dotnet format --verify-no-changes` are clean.