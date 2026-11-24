# Observability acceptance

- Date: 2026-11-24
- Update: ADR-0013 observability — a BCL `Meter`, eight Flow instruments, and governance span attributes, with no OpenTelemetry dependency
- Type: large
- Decision: accepted
- Decision owner: karlmoriaty
- Related demo: docs/demos/2026-11-24.md
- Related issue or PR: `docs/adr/0013-observability.md`

## Scope

- `FlowDiagnostics` adds eight public tag constants: `flow.outcome`, `step.name`, `budget.dimension`, `budget.limit`, `budget.used`, `approval.id`, `approval.decision`, and `cost.usd`.
- An internal `FlowMetrics` owns the `Meter` named `Runesgalder.Flow` (version 0.1.0) and eight instruments: `flow.runs`, `flow.steps`, `flow.step.duration`, `flow.tokens`, `flow.cost.usd`, `flow.budget.warnings`, `flow.budget.exceeded`, and `flow.approvals`.
- Run and step spans set `flow.outcome` and `step.name`; budget failures carry dimension, limit, and used; approval denials carry the identifier and decision.
- `FlowContext` gains an internal `FlowName` for metric tagging; the public constructor and members are unchanged.
- No OpenTelemetry SDK, exporter, or other package is referenced. Telemetry is off unless a host registers the source and meter. No payload, prompt, or completion text is recorded.

## Evidence

- Build: succeeded with 0 warnings and 0 errors.
- Tests: Flow 94 passed, 0 failed (9 new). MCP 61 passed with 3 Docker-only skipped; Sandbox 45 passed with 5 Docker-only skipped; Demo 5 passed.
- New metric tests cover the eight instruments: success and failure outcomes, charges with and without a budget, warn and hard-limit counters by dimension, and approved and denied decisions.
- New span tests cover the success outcome, step name, budget failure attributes on both spans, approval denial attributes on the run span, and the absence of input or output text in any tag.
- Format: `dotnet format Runesgalder.sln --verify-no-changes` clean.
- Security review: no new dependency, network, filesystem, process, or secret surface; instrumentation is in-process only.
- Secret scan: the change contains no credentials, tokens, or machine-specific paths.

## Risks

- Instrument and tag names become a compatibility surface within 0.1.x; renames require a new ADR.
- Token and USD counters inherit the accuracy of host charging, the same limitation the budget feature carries.
- Metrics deliberately omit run and step identifiers, so per-run questions are answered from traces.
- Span limit and used values are recorded as doubles, while journals keep decimals.
- Sandbox and MCP remain uninstrumented until their own records land.

## Follow-ups

- Write `docs/observability.md` with the mapping, a host enablement snippet, and the local-only default before the v0.1-preview gate.
- Add Sandbox, compile, and MCP instrumentation under their own ADRs, including `policy.decision` and `sandbox.backend`.
- Re-run the full suite on Ubuntu and Windows CI for the implementing commit.

## Decision notes

- Accepted in chat on 2026-11-24 with the recommended option: instrumentation only, no new dependency, BCL `Meter` plus the existing `ActivitySource`, exporters left to the host.

## Implementation evidence (same day)

- `FlowMetrics` implemented and wired into run completion, step completion, charging, budget scopes, wall warnings, and approval decisions.
- `FlowDiagnostics` tag constants added; span attributes set at the same points that already write the journal.
- Flow tests: 94 passed (previously 85).