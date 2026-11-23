# ADR-0001: One governed runtime for Flow and Sandbox

- Status: Accepted
- Date: 2026-11-23
- Deciders: karlmoriaty

## Context

Runesgalder has two product surfaces: typed agent orchestration (Flow) and safe execution of AI-generated C# (Sandbox). They could be built as two independent products, or as one runtime with two surfaces.

The project's central claim is that every effect is governed. Model calls, tool calls, code execution, and human steps need the same policy, approval, journal, artefact, and telemetry semantics. Two independent stacks would duplicate those semantics and drift.

## Decision

Flow and Sandbox share one runtime model:

- `Step` is the unit of typed work.
- `Effect` describes an observable side effect.
- `Policy` evaluates whether an effect may proceed.
- `Budget` bounds cost, tokens, time, and resources.
- `Approval` records a human decision.
- `Journal` is the append-only event history.
- `Artifact` is content-addressed output.
- `RunEvent` is the observable event stream.
- OpenTelemetry is the tracing and metrics boundary.

Code execution is an effect. The Sandbox is the isolation mechanism for that effect, not a separate orchestration universe.

Flow and Sandbox remain first-class product surfaces. Sharing a runtime does not mean sharing a single user experience or a single API.

## Consequences

Positive:

- One event model and one audit trail for every effect.
- Policy and approval do not need to be reinvented for code execution.
- Observability, replay, and testing can be developed once.
- The two surfaces can be demonstrated together in the data-analysis code-interpreter scenario.

Negative:

- The shared primitives must remain stable enough for both surfaces.
- A change to the journal, policy, or event model has a larger blast radius.
- The v0.1 scope must stay narrow to avoid building a platform before the core works.

## Alternatives considered

- **Two independent products.** Rejected: duplicate governance, fragmented observability, and a weaker story for the killer demo.
- **A generic orchestrator with a sandbox plug-in.** Rejected: the sandbox would remain a second-class effect and security semantics would be easy to bypass.
- **A sandbox-only product with orchestration added later.** Rejected: the orchestration API is a co-equal product requirement and the integration is part of the value.
